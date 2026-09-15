using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// The two-tenant test, and the zone decision it defends. Guild rank names LOOK like global data, and
// are tenant-scoped anyway because nobody can read them from Blizzard — a person types them, and a
// person typing is not public Blizzard data whatever it describes. Every case shares ONE global Guild
// between two communities to pin that.
[Collection("AegisScribe API")]
public class GuildRankNameIsolationTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task TwoCommunitiesFollowingOneGuild_KeepTheirOwnNamesForItsRanks()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedGuildAsync();

        await LinkGuildAsync(scenario.TenantA.Tenant.Id, shared.Id);
        await LinkGuildAsync(scenario.TenantB.Tenant.Id, shared.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(scenario.TenantA, shared.Id, 3, "Veteran")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(scenario.TenantB, shared.Id, 3, "Raider")).StatusCode);

        // The same guild, the same rank number, two different names — and each community sees only its
        // own. If this were global data, the second write would have overwritten the first.
        Assert.Equal("Veteran", await NameForAsync(scenario.TenantA, shared.Id, 3));
        Assert.Equal("Raider", await NameForAsync(scenario.TenantB, shared.Id, 3));
    }

    [Fact]
    public async Task ACommunityCannotNameTheRanksOfAGuildItDoesNotFollow()
    {
        // The rule that keeps the table honest. Without it an officer could write rows about any guild
        // in the database, and those rows would then be invisible because nothing joins to them.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildAsync();

        await LinkGuildAsync(scenario.TenantA.Tenant.Id, guild.Id);

        // B knows the guild id — it is global, so guessing it is not the leak. Acting on it is.
        var response = await SetAsync(scenario.TenantB, guild.Id, 3, "Raider");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And A's names are untouched.
        Assert.Empty(await ListAsync(scenario.TenantB));
    }

    [Fact]
    public async Task ACachedNameListPopulatedByOneCommunity_IsNotServedToAnother()
    {
        // The cache-key half of tenancy.md's testing section. This read IS cached
        // (t:{tenantId}:guild-rank-names), and a bare key would pass every other test in this file
        // and fail only this one — by telling B what A calls the ranks of a guild they both follow.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedGuildAsync();

        await LinkGuildAsync(scenario.TenantA.Tenant.Id, shared.Id);
        await LinkGuildAsync(scenario.TenantB.Tenant.Id, shared.Id);

        await SetAsync(scenario.TenantA, shared.Id, 3, "Veteran");

        // A reads first, populating its entry. B's read must miss it entirely and see its own
        // unnamed rank rather than A's name for it.
        Assert.Equal("Veteran", await NameForAsync(scenario.TenantA, shared.Id, 3));
        Assert.Null(await NameForAsync(scenario.TenantB, shared.Id, 3));

        // And B naming it does not disturb A's cached answer.
        await SetAsync(scenario.TenantB, shared.Id, 3, "Raider");

        Assert.Equal("Veteran", await NameForAsync(scenario.TenantA, shared.Id, 3));
        Assert.Equal("Raider", await NameForAsync(scenario.TenantB, shared.Id, 3));
    }

    [Fact]
    public async Task ReachingAnotherCommunitysRankNameRoute_Is404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        // 404, never 403 — a 403 would confirm the community exists (tenancy.md).
        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/guild-rank-names");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AMemberMayReadTheNamesButNotSetThem()
    {
        // Reads are TenantMember because the roster shows these to everyone who can see the roster;
        // naming them is the officers' job.
        var officer = await TenantSide.CreateAsync(fixture, "Rank Names", TenantRole.Officer);
        var member = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);
        var guild = await SeedGuildAsync();
        await LinkGuildAsync(officer.Tenant.Id, guild.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(officer, guild.Id, 0, "Guild Master")).StatusCode);

        Assert.Equal("Guild Master", await NameForAsync(member, guild.Id, 0));
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(member, guild.Id, 1, "Officer")).StatusCode);
    }

    [Fact]
    public async Task EveryRankZeroToNineIsListed_NamedOrNot()
    {
        // The editor is a form over a fixed range, so an unnamed rank is a blank to fill in rather than
        // a row that is missing.
        var officer = await TenantSide.CreateAsync(fixture, "Rank Names Range", TenantRole.Officer);
        var guild = await SeedGuildAsync();
        await LinkGuildAsync(officer.Tenant.Id, guild.Id);

        await SetAsync(officer, guild.Id, 3, "Veteran");

        var names = await ListAsync(officer);

        Assert.Equal(10, names.Count);
        Assert.Equal(Enumerable.Range(0, 10), names.Select(n => n.Rank).Order());
        Assert.Equal("Veteran", Assert.Single(names, n => n.Rank == 3).Name);
        Assert.Null(Assert.Single(names, n => n.Rank == 4).Name);
    }

    [Fact]
    public async Task ClearingANameRemovesTheRowRatherThanStoringAnEmptyString()
    {
        // "Never named" and "named then cleared" are the same state, so the UI has one blank to render
        // rather than two that look alike and behave differently.
        var officer = await TenantSide.CreateAsync(fixture, "Rank Names Clear", TenantRole.Officer);
        var guild = await SeedGuildAsync();
        await LinkGuildAsync(officer.Tenant.Id, guild.Id);

        await SetAsync(officer, guild.Id, 3, "Veteran");
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(officer, guild.Id, 3, null)).StatusCode);

        Assert.Null(await NameForAsync(officer, guild.Id, 3));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        Assert.False(await verify.GuildRankNames.AnyAsync(row => row.GuildId == guild.Id));
    }

    [Fact]
    public async Task ARankOutsideZeroToNine_IsAValidationProblem()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank Names Range Check", TenantRole.Officer);
        var guild = await SeedGuildAsync();
        await LinkGuildAsync(officer.Tenant.Id, guild.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(officer, guild.Id, 10, "Nope")).StatusCode);
    }

    private static Task<HttpResponseMessage> SetAsync(TenantSide side, Guid guildId, int rank, string? name) =>
        side.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{side.Tenant.Slug}/guild-rank-names/{guildId}/{rank}",
            new SetGuildRankNameViewModel { Name = name });

    private static async Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/guild-rank-names");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<GuildRankNameServiceModel>>())!;
    }

    private static async Task<string?> NameForAsync(TenantSide side, Guid guildId, int rank) =>
        (await ListAsync(side)).Single(row => row.GuildId == guildId && row.Rank == rank).Name;

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
