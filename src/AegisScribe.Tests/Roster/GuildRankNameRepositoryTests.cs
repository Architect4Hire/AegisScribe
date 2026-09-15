using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Roster;

// Repository: real SQL Server through the Aspire fixture. Two of the claims here only exist in the
// database — the unique index that makes the write an upsert rather than a duplicate factory, and the
// query filters that scope both halves of the 10-row-per-guild grid.
[Collection("AegisScribe API")]
public class GuildRankNameRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task List_ReturnsTenRowsPerFollowedGuild_NamedOrNot()
    {
        // The editor is a form over a fixed range, so an unnamed rank is a blank to fill in rather
        // than a row that is missing. That means the repository invents the rows Blizzard's 0-9 range
        // implies, rather than returning only what is stored.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var guild = await SeedFollowedGuildAsync(tenant.Id);

        await SetAsync(tenant.Id, guild.Id, 0, "Guild Master");

        var rows = await ListAsync(tenant.Id);

        Assert.Equal(10, rows.Count);
        Assert.Equal(Enumerable.Range(0, 10), rows.Select(row => row.Rank).Order());
        Assert.Equal("Guild Master", Assert.Single(rows, row => row.Rank == 0).Name);
        Assert.Null(Assert.Single(rows, row => row.Rank == 5).Name);
        Assert.All(rows, row => Assert.Equal(guild.Name, row.GuildName));
    }

    [Fact]
    public async Task List_ShowsNothingForAGuildTheCommunityDoesNotFollow()
    {
        // The grid is built from TenantGuilds, not from the global Guild table — a community only
        // names the ranks of guilds it actually follows.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedGuildAsync();

        Assert.Empty(await ListAsync(tenant.Id));
    }

    [Fact]
    public async Task Set_IsAnUpsert_NotADuplicateFactory()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var guild = await SeedFollowedGuildAsync(tenant.Id);

        await SetAsync(tenant.Id, guild.Id, 3, "Veteran");
        await SetAsync(tenant.Id, guild.Id, 3, "Raider");

        // One row, renamed — the unique index on (TenantId, GuildId, Rank) is what makes the second
        // write an update rather than a second row nobody can tell apart from the first.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var stored = await verify.GuildRankNames.Where(row => row.GuildId == guild.Id).ToListAsync();

        Assert.Equal("Raider", Assert.Single(stored).Name);
    }

    [Fact]
    public async Task Set_ANullOrBlankName_RemovesTheRow()
    {
        // "Never named" and "named then cleared" become the same state at rest, so the UI has one
        // blank to render rather than two that look alike and behave differently.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var guild = await SeedFollowedGuildAsync(tenant.Id);

        await SetAsync(tenant.Id, guild.Id, 3, "Veteran");
        await SetAsync(tenant.Id, guild.Id, 3, "   ");

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        Assert.False(await verify.GuildRankNames.AnyAsync(row => row.GuildId == guild.Id));
    }

    [Fact]
    public async Task FollowsGuild_IsScopedToTheAmbientCommunity()
    {
        // The guard behind the write, and it has to be query-filtered: another community following a
        // guild must not make it nameable here.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);
        var guild = await SeedFollowedGuildAsync(tenantA.Id);

        await using (var aDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantA.Id))
        {
            Assert.True(await new GuildRankNameRepository(aDb).FollowsGuildAsync(guild.Id, CancellationToken.None));
        }

        await using var bDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantB.Id);
        Assert.False(await new GuildRankNameRepository(bDb).FollowsGuildAsync(guild.Id, CancellationToken.None));
    }

    private async Task<IReadOnlyList<Domain.Managers.Models.ServiceModels.GuildRankNameServiceModel>> ListAsync(
        Guid tenantId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        return await new GuildRankNameRepository(db).ListAsync(CancellationToken.None);
    }

    private async Task SetAsync(Guid tenantId, Guid guildId, int rank, string? name)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        await new GuildRankNameRepository(db).SetAsync(guildId, rank, name, CancellationToken.None);
    }

    private async Task<Guild> SeedFollowedGuildAsync(Guid tenantId)
    {
        var guild = await SeedGuildAsync();

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        db.TenantGuilds.Add(new TenantGuild
        {
            Id = Guid.NewGuid(),
            GuildId = guild.Id,
            LinkedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return guild;
    }

    // A global Guild on a global Realm — both facts about the world, shared by every community that
    // follows them.
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
}
