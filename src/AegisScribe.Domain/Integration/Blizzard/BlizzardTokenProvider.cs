using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

// The client-credentials token lifecycle: cache it, and refresh it once under a lock rather than once
// per in-flight request (external.md). Registered as a singleton — the cache is worthless if every
// scope gets its own.
public sealed class BlizzardTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<BlizzardOptions> options,
    TimeProvider timeProvider,
    ILogger<BlizzardTokenProvider> logger) : IBlizzardTokenProvider
{
    // One mint at a time. The fast path below never touches this; only a caller that finds the cache
    // cold or stale waits on it.
    private readonly SemaphoreSlim _mintLock = new(1, 1);

    // Written only under _mintLock, read without it on the fast path — hence volatile.
    private volatile CachedToken? _cached;

    // Read and written only under _mintLock.
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;

    private int _missingCredentialsLogged;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.IsConfigured)
        {
            LogMissingCredentialsOnce();
            return null;
        }

        // Fast path: a token we already know is good, with no lock and no I/O. This is the
        // overwhelmingly common case — Blizzard's client-credentials tokens last about a day.
        var cached = _cached;
        if (cached is not null && timeProvider.GetUtcNow() < cached.RefreshAt)
        {
            return cached.AccessToken;
        }

        await _mintLock.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();

            // Re-check under the lock. Fifty callers can arrive together on a cold cache; the first
            // mints and the other forty-nine must use that result rather than mint their own. This
            // re-check is what makes "refresh once, under a lock" true rather than approximately true.
            cached = _cached;
            if (cached is not null && now < cached.RefreshAt)
            {
                return cached.AccessToken;
            }

            // A recent mint failed. Don't turn a battle.net outage into a tight loop against their
            // token endpoint; keep serving the old token if we still have one.
            if (now < _nextAttemptAt)
            {
                return cached?.AccessToken;
            }

            var minted = await RequestTokenAsync(settings, cancellationToken);

            if (minted is null)
            {
                _nextAttemptAt = now + settings.TokenFailureCooldown;

                // A failed refresh must never evict a token that still works. Anything we are holding
                // is at most TokenRefreshSkew from its stated expiry, so it is very likely still good
                // at Blizzard — and a stale token beats no token.
                return cached?.AccessToken;
            }

            _nextAttemptAt = DateTimeOffset.MinValue;
            _cached = minted;
            return minted.AccessToken;
        }
        finally
        {
            _mintLock.Release();
        }
    }

    private async Task<CachedToken?> RequestTokenAsync(BlizzardOptions settings, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(BlizzardDefaults.OAuthHttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            // HTTP Basic is how the client-credentials grant carries its credentials here, so they never
            // appear in a URL or a request body.
            Headers = { Authorization = BuildBasicCredential(settings) },
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
        };

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Status code only. A failed token response can echo request detail back, and this is
                // the log line most likely to be pasted into an issue.
                logger.LogWarning(
                    "Blizzard rejected the client-credentials request with {StatusCode}. Blizzard-backed data is unavailable.",
                    (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                logger.LogWarning("Blizzard returned a client-credentials response carrying no token. Blizzard-backed data is unavailable.");
                return null;
            }

            return new CachedToken(payload.AccessToken, CalculateRefreshAt(payload.ExpiresInSeconds, settings));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not reach the Blizzard token endpoint. Blizzard-backed data is unavailable.");
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The request timed out, as opposed to our caller giving up — that one propagates.
            logger.LogWarning(exception, "The Blizzard token request timed out. Blizzard-backed data is unavailable.");
            return null;
        }
    }

    private DateTimeOffset CalculateRefreshAt(int expiresInSeconds, BlizzardOptions settings)
    {
        // Read the lifetime Blizzard actually sent rather than assuming the usual ~24h
        // (references/blizzard-endpoints.md says read the value). A response with no usable lifetime is
        // treated as a short one rather than an eternal one.
        var lifetime = expiresInSeconds > 0
            ? TimeSpan.FromSeconds(expiresInSeconds)
            : settings.TokenRefreshSkew;

        // Renew ahead of expiry, but never land on or before now: a lifetime shorter than the skew would
        // otherwise mark the token stale the instant we stored it, and every single request would mint a
        // new one. Half the lifetime is the floor.
        var refreshAfter = lifetime > settings.TokenRefreshSkew
            ? lifetime - settings.TokenRefreshSkew
            : lifetime / 2;

        return timeProvider.GetUtcNow() + refreshAfter;
    }

    private static AuthenticationHeaderValue BuildBasicCredential(BlizzardOptions settings)
    {
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}"));
        return new AuthenticationHeaderValue("Basic", credential);
    }

    private void LogMissingCredentialsOnce()
    {
        // Once per process. Every cache-first read asks for a token, so this must not become one warning
        // per request.
        //
        // BlizzardStartupLogger is what normally reports missing credentials, because BlizzardGateway
        // returns on IsConfigured before reaching here. This stays as the backstop for a caller that holds
        // the token provider directly — a tool or a test host with no hosted services.
        if (Interlocked.Exchange(ref _missingCredentialsLogged, 1) == 0)
        {
            logger.LogWarning(
                "No Blizzard credentials are configured, so Blizzard-backed data is unavailable and the app is " +
                "serving stored data only. Set the blizzard-client-id and blizzard-client-secret Aspire " +
                "parameters (user secrets on the AppHost) to enable it.");
        }
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAt);

    // Blizzard's wire shape for a token response, and it never leaves this folder (external.md).
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);
}
