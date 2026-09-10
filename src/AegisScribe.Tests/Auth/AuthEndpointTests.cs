using System.Net;
using System.Net.Http.Json;
using AegisScribe.ApiService.Managers.Models.ServiceModels;

namespace AegisScribe.Tests.Auth;

[Collection("AegisScribe API")]
public class AuthEndpointTests(AegisScribeAppFixture fixture)
{
    private static string UniqueEmail() => $"test-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_CreatesUser()
    {
        var email = UniqueEmail();

        var response = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "Sup3r$ecretPwd!",
            displayName = "Test User",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserServiceModel>();
        Assert.NotNull(user);
        Assert.Equal(email, user!.Email);
        Assert.Equal("Test User", user.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(user.Id));
    }

    [Fact]
    public async Task Register_SameEmailTwice_SecondFailsBecauseTheFirstRoundTrippedThroughTheStore()
    {
        var email = UniqueEmail();
        var body = new { email, password = "Sup3r$ecretPwd!", displayName = "Dup Test" };

        var first = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Only reachable if the first registration was actually persisted and is queryable by
        // email — this is the "user store round-trips" assertion.
        var second = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_Succeeds()
    {
        var email = UniqueEmail();
        const string originalPassword = "Original$Pwd123!";
        const string newPassword = "Replacement$Pwd456!";

        var register = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = originalPassword,
            displayName = "Password Test",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var change = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            email,
            currentPassword = originalPassword,
            newPassword,
        });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Fails()
    {
        var email = UniqueEmail();

        var register = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "Correct$Pwd123!",
            displayName = "Password Test",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var change = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            email,
            currentPassword = "Wrong$Pwd999!",
            newPassword = "Whatever$Pwd789!",
        });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_CalledAnonymously_ReturnsBareUnauthorized()
    {
        // .NET 10 no longer 302-redirects a failed API auth to a login page (auth.md) — this must
        // be a bare 401, not a redirect and not a 500 from an unconfigured challenge scheme.
        var response = await fixture.ApiClient.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
