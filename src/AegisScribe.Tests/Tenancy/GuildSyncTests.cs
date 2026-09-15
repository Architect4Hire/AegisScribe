using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AegisScribe.Tests.Tenancy;

// 6.6b's sync, against real SQL with only Blizzard stubbed. The diff — joins, leaves, rank changes —
// and the character rows a roster has to create before it can point at anything.
[Collection("AegisScribe API")]
public class GuildSyncTests(AegisScribeAppFixture fixture)
{
    private const string Region = "us";

    [Fact]
    public async Task SyncingANewGuild_CreatesTheGuildItsMembersAndTheCharactersTheyNeed()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var gateway = StubGateway(realm.Slug, Roster(realm.Slug, ("Aldric", 1, 0), ("Brynhild", 2, 1)));

        var guild = await SyncAsync(gateway, realm.Slug, "Aegis Vigil");

        Assert.NotNull(guild);

        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        var members = await verify.GuildMembers
            .Where(m => m.GuildId == guild!.Id)
            .Include(m => m.Character)
            .ToListAsync();

        Assert.Equal(2, members.Count);

        // The characters did not exist before. The roster carries enough to create them, which is the
        // whole reason a 400-member guild costs one call instead of four hundred.
        Assert.Contains(members, m => m.Character.Name == "Aldric" && m.BlizzardRank == 0);
        Assert.Contains(members, m => m.Character.Name == "Brynhild" && m.BlizzardRank == 1);

        // And they carry no gear — which is correct, not a gap. The refresh worker already selects
        // characters with no equipment, so gear arrives later under the global limiter rather than as
        // an 800-call fan-out here.
        Assert.All(members, m => Assert.Equal(0, m.Character.ItemLevel));
    }

    [Fact]
    public async Task ResyncingDiffsTheRoster_AddingJoinersRemovingLeaversAndUpdatingRanks()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);

        var first = StubGateway(realm.Slug, Roster(realm.Slug, ("Aldric", 1, 0), ("Corvin", 4, 5), ("Delphine", 5, 5)));
        var guild = await SyncAsync(first, realm.Slug, "Aegis Vigil");

        // Corvin was promoted, Delphine gquit, Ithralas joined.
        var second = StubGateway(realm.Slug, Roster(realm.Slug, ("Aldric", 1, 0), ("Corvin", 4, 1), ("Ithralas", 3, 5)));
        await SyncAsync(second, realm.Slug, "Aegis Vigil");

        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        var members = await verify.GuildMembers
            .Where(m => m.GuildId == guild!.Id)
            .Include(m => m.Character)
            .ToListAsync();

        Assert.Equal(3, members.Count);
        Assert.Equal(1, Assert.Single(members, m => m.Character.Name == "Corvin").BlizzardRank);
        Assert.Contains(members, m => m.Character.Name == "Ithralas");

        // The leaver is gone from the guild — a roster that only ever grew would show people who quit.
        Assert.DoesNotContain(members, m => m.Character.Name == "Delphine");

        // But the Character row survives. She still exists, may be in another guild, and other
        // communities may roster her: GuildMember is the membership, not the person.
        Assert.True(await verify.Characters.AnyAsync(c => c.NameLower == "delphine"));
    }

    [Fact]
    public async Task ResyncingIdenticalData_ChangesNothingButLastSyncedAt()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var roster = Roster(realm.Slug, ("Aldric", 1, 0), ("Corvin", 4, 5));

        var guild = await SyncAsync(StubGateway(realm.Slug, roster), realm.Slug, "Aegis Vigil");

        List<Guid> firstIds;
        DateTimeOffset firstSyncedAt;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            firstIds = await db.GuildMembers.Where(m => m.GuildId == guild!.Id).Select(m => m.Id).ToListAsync();
            firstSyncedAt = (await db.Guilds.SingleAsync(g => g.Id == guild!.Id)).LastSyncedAt;
        }

        await SyncAsync(StubGateway(realm.Slug, roster, syncedAt: DateTimeOffset.UtcNow.AddHours(1)), realm.Slug, "Aegis Vigil");

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var afterIds = await verify.GuildMembers.Where(m => m.GuildId == guild!.Id).Select(m => m.Id).ToListAsync();

            // Same rows, not replacements — the membership is reconciled in place, so a second pass over
            // identical data churns nothing.
            Assert.Equal(firstIds.Order(), afterIds.Order());

            // LastSyncedAt MUST move: it is the thirty-day compliance clock, not a change marker.
            Assert.True((await verify.Guilds.SingleAsync(g => g.Id == guild!.Id)).LastSyncedAt > firstSyncedAt);
        }
    }

    [Fact]
    public async Task SyncingAGuild_NeverFetchesACharacter()
    {
        // The restriction, asserted. One call for the whole roster; the obvious follow-up of fetching
        // each member's gear would turn that into twice the roster size, which is exactly what the
        // per-tenant budget exists to stop.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var gateway = StubGateway(realm.Slug, Roster(realm.Slug, ("Aldric", 1, 0), ("Corvin", 4, 5)));

        await SyncAsync(gateway, realm.Slug, "Aegis Vigil");

        await gateway.Received(1).FetchGuildRosterAsync(realm.Slug, "aegis-vigil", Arg.Any<CancellationToken>());
        await gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
        await gateway.DidNotReceiveWithAnyArgs().FetchEquipmentAsync(default!, default!, default);
    }

    [Fact]
    public async Task SyncingAGuildBlizzardDoesNotKnow_StoresNothing()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var gateway = Substitute.For<IBlizzardGateway>();
        gateway.IsConfigured.Returns(true);
        gateway.FetchGuildRosterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GuildRosterSnapshot?)null);

        Assert.Null(await SyncAsync(gateway, realm.Slug, "No Such Guild"));

        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.False(await verify.Guilds.AnyAsync(g => g.NameLower == "no such guild"));
    }

    private async Task<Guild?> SyncAsync(IBlizzardGateway gateway, string realmSlug, string guildName)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);

        var dataLayer = new GuildSyncDataLayer(
            gateway,
            new GuildRepository(db),
            new RealmRepository(db),
            new CharacterRepository(db),
            NullLogger<GuildSyncDataLayer>.Instance);

        return await dataLayer.SyncAsync(Region, realmSlug, guildName, CancellationToken.None);
    }

    private static IBlizzardGateway StubGateway(string realmSlug, GuildRosterSnapshot roster, DateTimeOffset? syncedAt = null)
    {
        var gateway = Substitute.For<IBlizzardGateway>();
        gateway.IsConfigured.Returns(true);

        var stamped = syncedAt is null
            ? roster
            : roster with { Guild = Restamp(roster.Guild, syncedAt.Value) };

        gateway.FetchGuildRosterAsync(realmSlug, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(stamped);

        return gateway;
    }

    private static Guild Restamp(Guild guild, DateTimeOffset syncedAt) => new()
    {
        Name = guild.Name,
        NameLower = guild.NameLower,
        Faction = guild.Faction,
        BlizzardGuildId = guild.BlizzardGuildId,
        LastSyncedAt = syncedAt,
    };

    // A roster as the gateway would hand it over: domain entities, no realm ids, no gear.
    private static GuildRosterSnapshot Roster(string realmSlug, params (string Name, int ClassId, int Rank)[] members)
    {
        var now = DateTimeOffset.UtcNow;

        return new GuildRosterSnapshot(
            new Guild
            {
                Name = "Aegis Vigil",
                NameLower = "aegis vigil",
                Faction = CharacterFaction.Alliance,
                BlizzardGuildId = 88213714,
                LastSyncedAt = now,
            },
            [
                .. members.Select(member => new GuildRosterMembership(
                    new Domain.Managers.Models.Domain.Character
                    {
                        Name = member.Name,
                        NameLower = member.Name.ToLowerInvariant(),
                        Level = 80,
                        Class = (CharacterClass)member.ClassId,
                        Faction = CharacterFaction.Alliance,

                        // Stable per name, so a second pass over "the same roster" really is the same
                        // characters rather than new ones that would trivially insert.
                        BlizzardCharacterId = Math.Abs(member.Name.GetHashCode(StringComparison.Ordinal)) + 1_000_000L,
                        ItemLevel = 0,
                        LastSyncedAt = now,
                    },
                    realmSlug,
                    member.Rank)),
            ]);
    }
}
