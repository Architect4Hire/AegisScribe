using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Tenancy;

// 6.6's isolation test, and the one the prompt asks for by name: one community must not be able to
// starve another. Everything here goes through the endpoint rather than the repository, per
// tenancy.md's rule for this harness — a budget that isolates correctly in the data layer and leaks
// through a route is still a leak.
[Collection("AegisScribe API")]
public class SyncBudgetIsolationTests(AegisScribeAppFixture fixture) : IDisposable
{
    private const int RefreshesToExhaust =
        AegisScribeAppFixture.SyncBudgetCallsPerWindow / AegisScribeAppFixture.CallsPerCharacterRefresh;

    // A plain client, for the same reason PublicEdgeTests builds one: fixture.ApiClient carries the
    // standard resilience handler, which treats 429 as transient and retries it with backoff — and
    // honours Retry-After, which here is the rest of the budget window. A test trying to OBSERVE a 429
    // would sit waiting for a window that will not roll over, and fail on the handler's own timeout
    // rather than ever seeing the response.
    private readonly HttpClient _rawClient = new() { BaseAddress = fixture.ApiClient.BaseAddress };

    public void Dispose()
    {
        _rawClient.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExhaustingACommunitysBudget_LeavesTheOtherCommunityUntouched()
    {
        // Owners on both sides: Owner implies Officer, so the same user can both spend the budget and
        // read it.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        await ExhaustAsync(scenario.TenantA);

        // A is out...
        var aDenied = await RefreshAsync(scenario.TenantA);
        Assert.Equal(HttpStatusCode.TooManyRequests, aDenied.StatusCode);

        // ...and B is completely unaffected, which is the whole claim. If the budget were keyed on
        // anything but the resolved tenant — a global counter, a per-user counter, a cache key missing
        // its tenant prefix — this is the assertion that would fail.
        var bBudget = await ReadBudgetAsync(scenario.TenantB);
        Assert.Equal(AegisScribeAppFixture.SyncBudgetCallsPerWindow, bBudget.CallsRemaining);
        Assert.Equal(0, bBudget.CallsConsumed);

        var bRefresh = await RefreshAsync(scenario.TenantB);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, bRefresh.StatusCode);
    }

    [Fact]
    public async Task ACommunityCannotReadOrSpendAnotherCommunitysBudget()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        // 404, never 403 — a 403 would confirm the tenant exists (tenancy.md).
        var bReadsA = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/sync/budget");
        Assert.Equal(HttpStatusCode.NotFound, bReadsA.StatusCode);

        var bSpendsA = await scenario.TenantB.SendAsync(
            HttpMethod.Post, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/sync/characters/emberfall/nobody");
        Assert.Equal(HttpStatusCode.NotFound, bSpendsA.StatusCode);

        // And nothing was charged to A by B trying.
        var aBudget = await ReadBudgetAsync(scenario.TenantA);
        Assert.Equal(0, aBudget.CallsConsumed);
    }

    [Fact]
    public async Task SpendingTheBudget_IsVisibleToTheOwnerAsItHappens()
    {
        // external.md: the budget is visible to the tenant's owner. A limit nobody can see reads as a
        // bug the first time it bites.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var before = await ReadBudgetAsync(scenario.TenantA);
        Assert.Equal(AegisScribeAppFixture.SyncBudgetCallsPerWindow, before.CallsPerWindow);
        Assert.Equal(AegisScribeAppFixture.SyncBudgetCallsPerWindow, before.CallsRemaining);

        await RefreshAsync(scenario.TenantA);

        var after = await ReadBudgetAsync(scenario.TenantA);
        Assert.Equal(AegisScribeAppFixture.CallsPerCharacterRefresh, after.CallsConsumed);
        Assert.Equal(
            AegisScribeAppFixture.SyncBudgetCallsPerWindow - AegisScribeAppFixture.CallsPerCharacterRefresh,
            after.CallsRemaining);
        Assert.True(after.WindowResetsAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExhaustionIsA429WithARetryHintAndAMachineReadableType()
    {
        // "Budget exhaustion is a 429, not a silent queue" (external.md), and the Retry-After is the
        // half that stops a client retrying immediately and forever.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        await ExhaustAsync(scenario.TenantA);

        var denied = await RefreshAsync(scenario.TenantA);

        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        Assert.NotNull(denied.Headers.RetryAfter);
        Assert.True(denied.Headers.RetryAfter!.Delta > TimeSpan.Zero);

        // The `type` URI is the contract (api-contract.md). A client needs it to tell "your community
        // is out of budget" from the API's own per-IP throttling, which is also a 429 and means
        // something completely different.
        var problem = await denied.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.Equal("https://api.aegisscribe.com/problems/tenant-sync-budget-exhausted", problem!.Type);
    }

    [Fact]
    public async Task AMemberCannotReadTheBudgetAndCannotSpendIt()
    {
        // The budget read is Owner-only; the refresh is Officer-level. A plain Member is below both.
        var scenario = await TwoTenantFixture.CreateAsync(
            fixture, roleA: TenantRole.Member, roleB: TenantRole.Owner);

        var read = await scenario.TenantA.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/sync/budget");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var spend = await RefreshAsync(scenario.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, spend.StatusCode);
    }

    private async Task ExhaustAsync(TenantSide side)
    {
        for (var i = 0; i < RefreshesToExhaust; i++)
        {
            var response = await RefreshAsync(side);

            // Whether Blizzard answered is irrelevant — the calls were made and the budget is charged
            // either way. What must NOT happen is a 429 before the budget is actually spent.
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private Task<HttpResponseMessage> RefreshAsync(TenantSide side) =>
        SendAsync(side, HttpMethod.Post, $"/api/v1/t/{side.Tenant.Slug}/sync/characters/emberfall/nobody");

    // The endpoint remains the only door (tenancy.md) — this just skips the retry wrapper, not the API.
    private async Task<HttpResponseMessage> SendAsync(TenantSide side, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", side.AccessToken);

        return await _rawClient.SendAsync(request);
    }

    private async Task<SyncBudgetServiceModel> ReadBudgetAsync(TenantSide side)
    {
        var response = await SendAsync(side, HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/sync/budget");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SyncBudgetServiceModel>())!;
    }

    private sealed record ProblemDetailsBody(string? Type, string? Title, int? Status);
}
