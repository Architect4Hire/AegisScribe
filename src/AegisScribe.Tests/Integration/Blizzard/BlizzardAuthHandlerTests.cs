using System.Net;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Tests.Infrastructure;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — the handler that puts the token on every outbound request, so no gateway method has to remember
// to (.claude/rules/external.md; add-external-sync skill step 3).
public class BlizzardAuthHandlerTests
{
    [Fact]
    public async Task Send_WithAToken_SetsTheBearerHeader()
    {
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, token: "token-one");

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        var request = inner.SingleRequest;
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("token-one", request.AuthorizationParameter);
    }

    [Fact]
    public async Task Send_WithAToken_NeverPutsTheTokenInTheQueryString()
    {
        // Blizzard disallowed the query-parameter form in 2024 and it will fail; it also leaks the token
        // into every log and referer along the way, which is why the repo's secret-guard hook blocks the
        // literal outright (external.md). The handler is the reason no call site can reintroduce it.
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, token: "token-one");

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        var request = inner.SingleRequest;
        Assert.DoesNotContain("token-one", request.Query);
        Assert.DoesNotContain("access", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("?namespace=dynamic-us&locale=en_US", request.Query);
    }

    [Fact]
    public async Task Send_WithNoTokenAvailable_ShortCircuitsAndSendsNothingOutbound()
    {
        // Degrades rather than throwing, and — the part that matters — never sends an unauthenticated
        // request to Blizzard, whose 401 a caller could mistake for a real answer.
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, token: null);

        var response = await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, inner.RequestCount);
    }

    private static HttpClient CreateClient(StubHttpMessageHandler inner, string? token)
    {
        var tokenProvider = Substitute.For<IBlizzardTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(token);

        var handler = new BlizzardAuthHandler(tokenProvider) { InnerHandler = inner };

        return new HttpClient(handler) { BaseAddress = new Uri("https://us.api.blizzard.com") };
    }
}
