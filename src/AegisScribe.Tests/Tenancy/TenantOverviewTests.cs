using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;

namespace AegisScribe.Tests.Tenancy;

// 8.5's composed read — the five facts the first-run checklist is a pure function of.
//
// The thing worth defending here is that every field is DERIVED. There is no stored progress marker
// anywhere in this feature, so un-completing a step by deleting the thing that completed it has to
// work, and one of these tests does exactly that.
[Collection("AegisScribe API")]
public class TenantOverviewTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task AFreshCommunityHasNothingButItsOwner()
    {
        // The state 8.5 exists for: one member, no guild, no ranks, no roster.
        var owner = await TenantSide.CreateAsync(fixture, "Fresh", TenantRole.Owner);

        var overview = await GetOverviewAsync(owner);

        Assert.False(overview.HasLinkedGuild);
        Assert.Equal(0, overview.RankCount);
        Assert.Equal(0, overview.RosterCount);

        // One, not zero — the person reading this is themselves a member, and a checklist telling a
        // brand-new owner they have no members would be counting wrong.
        Assert.Equal(1, overview.MemberCount);
    }

    [Fact]
    public async Task EveryCountTracksWhatTheCommunityActuallyHas_InBothDirections()
    {
        var owner = await TenantSide.CreateAsync(fixture, "Counting", TenantRole.Owner);

        var guild = await SeedGuildAsync();
        await LinkGuildAsync(owner.Tenant.Id, guild.Id);
        var rankId = await SeedRankAsync(owner.Tenant.Id, "Raider");
        await SeedRosterEntryAsync(owner.Tenant.Id);
        await TenantSide.JoinAsync(fixture, owner.Tenant, TenantRole.Member);

        var set = await GetOverviewAsync(owner);
        Assert.True(set.HasLinkedGuild);
        Assert.Equal(1, set.RankCount);
        Assert.Equal(1, set.RosterCount);
        Assert.Equal(2, set.MemberCount);

        // And back down again. This is the whole reason the response carries facts rather than a step
        // number: an officer who deletes their only rank has, genuinely, not named their ranks — a
        // persisted "step 3 of 4" would still be claiming they had.
        await DeleteRankAsync(owner.Tenant.Id, rankId);

        var afterDelete = await GetOverviewAsync(owner);
        Assert.Equal(0, afterDelete.RankCount);
        Assert.True(afterDelete.HasLinkedGuild);
    }

    [Fact]
    public async Task TheBlizzardFlagReportsTheDeploymentsCredentials_NotTheCommunitys()
    {
        // Two communities in one deployment must agree about this: it is a fact about the host's
        // configuration, and a per-community answer would be meaningless. The test fixture runs with
        // no credentials (CLAUDE.md → Usage: the app runs against seeded data with none), which is why
        // this asserts false rather than merely asserting the two match.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        Assert.False((await GetOverviewAsync(scenario.TenantA)).BlizzardConfigured);
        Assert.False((await GetOverviewAsync(scenario.TenantB)).BlizzardConfigured);
    }

    [Fact]
    public async Task NeitherCommunitysSetupLeaksIntoTheOthersOverview()
    {
        // The two-tenant test. Four tenant-scoped tables are counted in one read, and a dropped filter
        // on any one of them shows up here as A's ranks or B's roster appearing on the wrong overview.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var guild = await SeedGuildAsync();
        await LinkGuildAsync(scenario.TenantA.Tenant.Id, guild.Id);
        await SeedRankAsync(scenario.TenantA.Tenant.Id, "Raider");
        await SeedRankAsync(scenario.TenantA.Tenant.Id, "Trial");
        await SeedRosterEntryAsync(scenario.TenantA.Tenant.Id);
        await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var a = await GetOverviewAsync(scenario.TenantA);
        Assert.True(a.HasLinkedGuild);
        Assert.Equal(2, a.RankCount);
        Assert.Equal(1, a.RosterCount);
        Assert.Equal(2, a.MemberCount);

        // B set nothing up, and must still look like a fresh community — not like A.
        var b = await GetOverviewAsync(scenario.TenantB);
        Assert.False(b.HasLinkedGuild);
        Assert.Equal(0, b.RankCount);
        Assert.Equal(0, b.RosterCount);
        Assert.Equal(1, b.MemberCount);
    }

    [Fact]
    public async Task ReachingAnotherCommunitysOverviewIs404NotForbidden()
    {
        // 404, never 403 — a 403 would confirm the community exists (tenancy.md).
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/overview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task APlainMemberIsForbidden()
    {
        // TenantOfficer, because this is a setup to-do list. A member has nothing to do with it and
        // gets their screens' ordinary empty states instead of somebody else's checklist.
        var officer = await TenantSide.CreateAsync(fixture, "Officer Only", TenantRole.Officer);
        var member = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{officer.Tenant.Slug}/overview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedIsUnauthorized()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);

        var response = await fixture.ApiClient.GetAsync($"/api/v1/t/{tenant.Slug}/overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- helpers ----

    private static async Task<TenantOverviewServiceModel> GetOverviewAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TenantOverviewServiceModel>())!;
    }

    private async Task<Guid> SeedRankAsync(Guid tenantId, string name)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var rank = new TenantRank
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = 10,
            Colour = "#cba76a",
        };

        db.TenantRanks.Add(rank);
        await db.SaveChangesAsync();

        return rank.Id;
    }

    private async Task DeleteRankAsync(Guid tenantId, Guid rankId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        db.TenantRanks.Remove(new TenantRank { Id = rankId });
        await db.SaveChangesAsync();
    }

    private async Task SeedRosterEntryAsync(Guid tenantId)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        db.RosterEntries.Add(new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

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

    private async Task LinkGuildAsync(Guid tenantId, Guid guildId)
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
}
