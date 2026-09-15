using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AegisScribe.Tests.Auth;
using Microsoft.AspNetCore.WebUtilities;

namespace AegisScribe.Tests.Gateway;

// Shared by every gateway integration test that needs a real, logged-in session: drives the full
// authorization-code + PKCE round trip by hand (GET /auth/login -> connect/authorize's sign-in form
// -> the form_post callback that actually sets the cookie) exactly once, so individual test classes
// don't each reimplement it.
internal static class GatewayLoginFlow
{
    public const string Password = "Sup3r$ecretPwd!";

    private static readonly Regex HiddenInputPattern =
        new("<input type=\"hidden\" name=\"(?<name>[^\"]+)\" value=\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    public static string UniqueEmail() => $"gw-test-{Guid.NewGuid():N}@example.com";

    public static async Task<string> RegisterUserAsync(AegisScribeAppFixture fixture) =>
        (await RegisterUserWithIdAsync(fixture)).Email;

    public static async Task<(string Email, string Id)> RegisterUserWithIdAsync(AegisScribeAppFixture fixture)
    {
        var email = UniqueEmail();
        var response = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Gateway Test User",
        });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Registration failed: {response.StatusCode}");
        }

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        var id = body!.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Registration response carried no id.");
        return (email, id);
    }

    // Drives the full authorization-code + PKCE round trip by hand: /auth/login → connect/authorize →
    // POST the sign-in form → the API replies 200 with a self-submitting form (response_mode is
    // form_post, not a redirect) → POST those fields to the callback, where the cookie is set.
    //
    // A plain handler with cookies on and redirects off is what lets each hop's headers be inspected
    // rather than silently followed.
    public static async Task<LoggedInClient> LoginAsync(
        AegisScribeAppFixture fixture, string email, int? tokenLifetimeSeconds = null)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false,
        };
        var http = new HttpClient(handler) { BaseAddress = fixture.GatewayBaseAddress };

        var loginResponse = await http.GetAsync("/auth/login");
        if (loginResponse.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"/auth/login did not redirect: {loginResponse.StatusCode}");
        }

        var authorizeUri = ResolveLocation(http, loginResponse);

        var oauthParams = QueryHelpers.ParseQuery(authorizeUri.Query);
        var formFields = oauthParams
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.ToString()))
            .ToList();
        formFields.Add(new("identifier", email));
        formFields.Add(new("credential", Password));
        if (tokenLifetimeSeconds is { } lifetime)
        {
            formFields.Add(new("token_lifetime_seconds", lifetime.ToString()));
        }

        var authorizeEndpoint = new Uri(authorizeUri.GetLeftPart(UriPartial.Path));
        var authorizeResponse = await http.PostAsync(authorizeEndpoint, new FormUrlEncodedContent(formFields));
        if (authorizeResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"connect/authorize sign-in failed: {authorizeResponse.StatusCode}");
        }

        var formHtml = await authorizeResponse.Content.ReadAsStringAsync();

        // The redirect_uri from the original request is exactly this form's action target — no
        // need to parse it back out of the HTML.
        var redirectUri = new Uri(oauthParams["redirect_uri"].ToString());
        var callbackFields = HiddenInputPattern
            .Matches(formHtml)
            .Select(m => new KeyValuePair<string, string>(
                WebUtility.HtmlDecode(m.Groups["name"].Value), WebUtility.HtmlDecode(m.Groups["value"].Value)))
            .ToList();

        var callbackResponse = await http.PostAsync(redirectUri, new FormUrlEncodedContent(callbackFields));
        if (callbackResponse.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"/auth/callback did not complete login: {callbackResponse.StatusCode}");
        }

        var setCookie = callbackResponse.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith("__Secure-", StringComparison.Ordinal))
            : null;
        if (setCookie is null)
        {
            throw new InvalidOperationException("The callback response carried no session cookie.");
        }

        // The session cookie's Domain does not domain-match localhost, so a real CookieContainer
        // silently never sends it back. Every subsequent authenticated call attaches it as an explicit
        // header instead — a gap a real deployment would not have once app./bff./api. are subdomains.
        var sessionCookie = setCookie[..setCookie.IndexOf(';')];

        return new LoggedInClient(http, email, setCookie, sessionCookie);
    }

    private static Uri ResolveLocation(HttpClient client, HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("Expected a Location header on a redirect response.");
        return location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);
    }
}

internal sealed class LoggedInClient(HttpClient http, string email, string setCookieHeader, string sessionCookie) : IDisposable
{
    public string Email { get; } = email;
    public string SetCookieHeader { get; } = setCookieHeader;

    public Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method, string path, params (string Name, string Value)[] extraHeaders)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", sessionCookie);
        foreach (var (name, value) in extraHeaders)
        {
            request.Headers.Add(name, value);
        }

        return http.SendAsync(request);
    }

    public void Dispose() => http.Dispose();
}
