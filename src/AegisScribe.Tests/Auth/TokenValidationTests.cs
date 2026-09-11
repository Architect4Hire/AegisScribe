using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AegisScribe.Tests.Auth;

[Collection("AegisScribe API")]
public class TokenValidationTests(AegisScribeAppFixture fixture)
{
    private async Task<string> MintTokenAsync(params (string Key, string Value)[] extraParameters)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", "aegisscribe-ops"),
            new("client_secret", fixture.OpsClientSecret),
        };
        parameters.AddRange(extraParameters.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

        var response = await fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(parameters));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return body!.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> CallWhoAmIAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/whoami");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await fixture.ApiClient.SendAsync(request);
    }

    [Fact]
    public async Task ValidToken_IsAccepted()
    {
        var token = await MintTokenAsync();

        var response = await CallWhoAmIAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("aegisscribe-ops", body!.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var token = await MintTokenAsync(("token_lifetime_seconds", "1"));
        await Task.Delay(TimeSpan.FromSeconds(2));

        var response = await CallWhoAmIAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudienceToken_IsRejected()
    {
        // aegisscribe-ops is granted permission for this resource (OpenIddictClientSeeder.cs) so the
        // token mints successfully, but it isn't "aegisscribe-api" — the only audience the API's own
        // AddAudiences() accepts — so validation must still reject it.
        var token = await MintTokenAsync(("resource", "https://wrong-audience.aegisscribe.example/"));

        var response = await CallWhoAmIAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedToken_IsRejected()
    {
        var token = await MintTokenAsync();
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var response = await CallWhoAmIAsync(tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
