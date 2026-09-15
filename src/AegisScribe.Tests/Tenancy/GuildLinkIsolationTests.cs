using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 6.6b's two-tenant test. The interesting case here is the opposite of the usual one: the GUILD is
// global and genuinely shared, while the LINK to it is private. Two communities following the same
// guild is normal; either one seeing the other's list of links is a leak.
[Collection("AegisScribe API")]
public class GuildLinkIsolationTests(AegisScribeAppFixture fixture) : IDisposable
{
    // The API serializes enums as their NAME (api-contract.md: "enums cross the wire as strings,
    // never as integers"), so a default deserializer cannot read GuildServiceModel.Faction.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Plain client: linking spends budget, and an exhausted budget 429s, which the resilience handler
    // would retry against a day-long Retry-After. Same reasoning as SyncBudgetIsolationTests.
    private readonly HttpClient _rawClient = new() { BaseAddress = fixture.ApiClient.BaseAddress };

    public void Dispose()
    {
        _rawClient.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TwoCommunitiesCanFollowTheSameGuild_AndNeitherSeesTheOthersLinks()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedGuildAsync();
        var onlyForB = await SeedGuildAsync();

        await LinkDirectlyAsync(scenario.TenantA.Tenant.Id, shared.Id);
        await LinkDirectlyAsync(scenario.TenantB.Tenant.Id, shared.Id);
        await LinkDirectlyAsync(scenario.TenantB.Tenant.Id, onlyForB.Id);

        var aList = await ListAsync(scenario.TenantA);
        var bList = await ListAsync(scenario.TenantB);

        // The shared guild is visible to both — it is global data and following it is not exclusive.
        Assert.Contains(aList, g => g.Id == shared.Id);
        Assert.Contains(bList, g => g.Id == shared.Id);

        // B's other link is B's business. If TenantGuild had no query filter, or the list read the
        // global Guilds table instead of the links, this is the assertion that would fail.
        Assert.DoesNotContain(aList, g => g.Id == onlyForB.Id);
        Assert.Single(aList);
    }

    [Fact]
    public async Task UnlinkingInOneCommunity_LeavesTheOtherCommunitysLinkAndTheGuildItself()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedGuildAsync();

        await LinkDirectlyAsync(scenario.TenantA.Tenant.Id, shared.Id);
        await LinkDirectlyAsync(scenario.TenantB.Tenant.Id, shared.Id);

        var unlink = await SendAsync(
            scenario.TenantA, HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/guilds/{shared.Id}");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        Assert.Empty(await ListAsync(scenario.TenantA));

        // B is untouched...
        Assert.Contains(await ListAsync(scenario.TenantB), g => g.Id == shared.Id);

        // ...and the guild itself survives, because it is global reference data and A walking away
        // from it says nothing about whether it exists.
        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.True(await verify.Guilds.AnyAsync(g => g.Id == shared.Id));
    }

    [Fact]
    public async Task ACommunityCannotUnlinkOrResyncAGuildItHasNotLinked()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var onlyForA = await SeedGuildAsync();

        await LinkDirectlyAsync(scenario.TenantA.Tenant.Id, onlyForA.Id);

        // B knows the guild id — it is global, so guessing it is not the leak. What B must not be able
        // to do is act on A's relationship with it, or spend B's budget refreshing it.
        var bUnlinks = await SendAsync(
            scenario.TenantB, HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/guilds/{onlyForA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, bUnlinks.StatusCode);

        var bResyncs = await SendAsync(
            scenario.TenantB, HttpMethod.Post, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/guilds/{onlyForA.Id}/sync");
        Assert.Equal(HttpStatusCode.NotFound, bResyncs.StatusCode);

        // A's link is still there.
        Assert.Contains(await ListAsync(scenario.TenantA), g => g.Id == onlyForA.Id);
    }

    [Fact]
    public async Task ReachingAnotherCommunitysGuildRoute_Is404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildAsync();
        await LinkDirectlyAsync(scenario.TenantA.Tenant.Id, guild.Id);

        // 404, never 403 — a 403 would confirm the tenant exists (tenancy.md).
        var response = await SendAsync(
            scenario.TenantB, HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/guilds");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AMemberCanSeeTheGuildsButNotChangeThem()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture, roleA: TenantRole.Member);
        var guild = await SeedGuildAsync();
        await LinkDirectlyAsync(scenario.TenantA.Tenant.Id, guild.Id);

        // Reads are TenantMember: which guilds the community follows is not privileged information
        // inside the community.
        Assert.Contains(await ListAsync(scenario.TenantA), g => g.Id == guild.Id);

        var unlink = await SendAsync(
            scenario.TenantA, HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/guilds/{guild.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, unlink.StatusCode);
    }

    // Seeds a global Guild directly. The link is what these tests are about; how the guild got there is
    // GuildSyncTests' subject.
    private async Task<Guild> SeedGuildAsync()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var name = $"Guild{Guid.NewGuid():N}"[..18];

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var guild = new Guild
        {
            Id = Guid.NewGuid(),
            RealmId = realm.Id,
            Name = name,
            NameLower = name.ToLowerInvariant(),
            Faction = CharacterFaction.Alliance,
            BlizzardGuildId = Random.Shared.NextInt64(1, long.MaxValue),
            LastSyncedAt = DateTimeOffset.UtcNow,
        };

        db.Guilds.Add(guild);
        await db.SaveChangesAsync();

        return guild;
    }

    // Links without going through the endpoint, because linking via HTTP would call Blizzard — which is
    // not configured in tests. What is under test here is isolation of the link, not how it is created.
    private async Task LinkDirectlyAsync(Guid tenantId, Guid guildId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        db.TenantGuilds.Add(new TenantGuild
        {
            Id = Guid.NewGuid(),
            GuildId = guildId,
            LinkedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<GuildServiceModel>> ListAsync(TenantSide side)
    {
        var response = await SendAsync(side, HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/guilds");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<GuildServiceModel>>(Json))!;
    }

    private async Task<HttpResponseMessage> SendAsync(TenantSide side, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", side.AccessToken);

        return await _rawClient.SendAsync(request);
    }
}
