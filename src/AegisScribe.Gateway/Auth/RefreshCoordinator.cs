using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;

namespace AegisScribe.Gateway.Auth;

// Single-flight refresh (gateway.md): a burst of proxied requests arriving as the token expires must
// produce one refresh, not twenty. The lock and the "recently refreshed" cache are per browser session,
// keyed by the session cookie's raw value, and live only in this gateway instance — a multi-instance
// deployment would need a distributed lock.
//
// Deliberately does NOT re-call HttpContext.AuthenticateAsync to notice a racer's completed refresh:
// that result is memoised on the HttpContext for the request, so a second call after acquiring the lock
// returns the SAME stale, pre-lock result. Keying the cache on the still-encrypted cookie value avoids
// decrypting the ticket to recover the real session id, which is an unexposed implementation detail.
public class RefreshCoordinator(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, (string AccessToken, DateTimeOffset ExpiresAtUtc)> _recentlyRefreshed = new();

    public async Task<string?> GetValidAccessTokenAsync(HttpContext httpContext)
    {
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Properties is null)
        {
            return null;
        }

        if (TryGetUnexpiredAccessToken(authResult.Properties, out var accessToken))
        {
            return accessToken;
        }

        var sessionKey = httpContext.Request.Cookies[GatewayAuthDefaults.CookieName];
        if (sessionKey is null)
        {
            return null;
        }

        if (TryGetRecentlyRefreshed(sessionKey, out accessToken))
        {
            return accessToken;
        }

        var gate = _locks.GetOrAdd(sessionKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // A waiter may find another racer already refreshed while it waited.
            if (TryGetRecentlyRefreshed(sessionKey, out accessToken))
            {
                return accessToken;
            }

            var refreshToken = authResult.Properties.GetTokenValue("refresh_token");
            if (refreshToken is null)
            {
                return null;
            }

            var newTokens = await RedeemRefreshTokenAsync(refreshToken);
            if (newTokens is null)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return null;
            }

            authResult.Properties.UpdateTokenValue("access_token", newTokens.Value.AccessToken);
            authResult.Properties.UpdateTokenValue("refresh_token", newTokens.Value.RefreshToken);
            authResult.Properties.UpdateTokenValue("expires_at", newTokens.Value.ExpiresAtUtc.ToString("o"));

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, authResult.Principal!, authResult.Properties);

            _recentlyRefreshed[sessionKey] = (newTokens.Value.AccessToken, newTokens.Value.ExpiresAtUtc);
            return newTokens.Value.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetRecentlyRefreshed(string sessionKey, out string? accessToken)
    {
        if (_recentlyRefreshed.TryGetValue(sessionKey, out var cached)
            && cached.ExpiresAtUtc - RefreshBuffer > DateTimeOffset.UtcNow)
        {
            accessToken = cached.AccessToken;
            return true;
        }

        accessToken = null;
        return false;
    }

    private static bool TryGetUnexpiredAccessToken(AuthenticationProperties properties, out string? accessToken)
    {
        accessToken = properties.GetTokenValue("access_token");
        var expiresAtRaw = properties.GetTokenValue("expires_at");

        return accessToken is not null
            && DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt)
            && expiresAt - RefreshBuffer > DateTimeOffset.UtcNow;
    }

    private async Task<(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc)?> RedeemRefreshTokenAsync(
        string refreshToken)
    {
        var bffSecret = configuration["Oidc:BffClientSecret"];
        var client = httpClientFactory.CreateClient(GatewayAuthDefaults.ApiBackchannelClient);

        var response = await client.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", GatewayAuthDefaults.ClientId),
            new("client_secret", bffSecret),
        ]));

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(raw);
        if (payload?.AccessToken is null || payload.RefreshToken is null)
        {
            return null;
        }

        return (payload.AccessToken, payload.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn));
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
