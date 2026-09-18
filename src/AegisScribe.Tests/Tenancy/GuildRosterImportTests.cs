using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 8.2 — importing a linked guild's members onto a community's roster.
//
// The import sits exactly on the seam between the two zones: it READS global data (GuildMember rows
// two communities share) and WRITES tenant-scoped data (RosterEntry rows neither may see of the
// other). So the two-tenant case here is the interesting one and it is deliberately run over the SAME
// guild — the configuration where a missing filter would look most plausible, because both sides
// genuinely should see the same members.
[Collection("AegisScribe API")]
public class GuildRosterImportTests(AegisScribeAppFixture fixture)
{
    // The API serializes enums as their NAME (api-contract.md), so a default deserializer cannot read
    // RosterEntryServiceModel.Class.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task ImportingAGuild_CreatesOneUnrankedUnclaimedMainPerMember()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 3);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        var result = await ImportAsync(scenario.TenantA, guild.GuildId);

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.AlreadyOnRoster);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var entries = await verify.RosterEntries.ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal(guild.CharacterIds.Order(), entries.Select(e => e.CharacterId).Order());

        foreach (var entry in entries)
        {
            // Unranked: BlizzardRank is what the GAME says, and it never derives into the community's
            // own ladder. Every seeded member has a non-zero BlizzardRank, so a row that borrowed it
            // would be visible here.
            Assert.Null(entry.TenantRankId);

            // A main. Who is somebody's alt is a judgement this community has not made yet.
            Assert.Null(entry.MainRosterEntryId);

            Assert.Null(entry.OfficerNote);
        }

        // No claims. Blizzard has no notion of which member plays which character, so every imported
        // row starts unclaimed and each member claims their own (7.2b).
        Assert.Empty(await verify.CharacterClaims.ToListAsync());
    }

    [Fact]
    public async Task ImportingCopiesNoCharacterData_OnlyAForeignKeyIntoTheGlobalZone()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 2);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        await using (var before = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            Assert.Equal(2, await before.Characters.CountAsync(c => guild.CharacterIds.Contains(c.Id)));
        }

        await ImportAsync(scenario.TenantA, guild.GuildId);

        // The global zone is untouched: no characters created, none modified. An import that copied
        // name, level or item level onto the tenant side would be the start of a per-tenant character
        // store, which is precisely what tenancy.md's two zones exist to prevent.
        await using var after = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.Equal(2, await after.Characters.CountAsync(c => guild.CharacterIds.Contains(c.Id)));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var entries = await verify.RosterEntries.ToListAsync();
        Assert.All(entries, entry => Assert.Contains(entry.CharacterId, guild.CharacterIds));
    }

    [Fact]
    public async Task ImportingTwice_AddsNothingAndKeepsEveryRankAndNote()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 3);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        var first = await ImportAsync(scenario.TenantA, guild.GuildId);
        Assert.Equal(3, first.Imported);

        // An officer does what an officer does between two imports: ranks somebody and writes a note.
        // These are the community's OWN data, and the whole idempotence requirement is that a second
        // import does not tread on them.
        var rankId = await SeedRankAsync(scenario.TenantA.Tenant.Id, "Raider", 10, "#cba76a");
        Guid rankedEntryId;

        await using (var edit = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            var entry = await edit.RosterEntries.FirstAsync();
            rankedEntryId = entry.Id;
            entry.TenantRankId = rankId;
            entry.OfficerNote = "Tanking Tuesdays.";
            await edit.SaveChangesAsync();
        }

        var second = await ImportAsync(scenario.TenantA, guild.GuildId);

        Assert.Equal(0, second.Imported);
        Assert.Equal(3, second.AlreadyOnRoster);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);

        // Not doubled...
        Assert.Equal(3, await verify.RosterEntries.CountAsync());

        // ...and the officer's work survived.
        var preserved = await verify.RosterEntries.FirstAsync(e => e.Id == rankedEntryId);
        Assert.Equal(rankId, preserved.TenantRankId);
        Assert.Equal("Tanking Tuesdays.", preserved.OfficerNote);
    }

    [Fact]
    public async Task AMemberWhoLeftTheGuild_KeepsTheirRosterEntryAndIsReportedInstead()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 2);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        await ImportAsync(scenario.TenantA, guild.GuildId);

        // Somebody gquits. The guild sync removes the GuildMember row — the roster entry is not its to
        // touch.
        var departed = guild.CharacterIds[0];

        await using (var leave = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var member = await leave.GuildMembers.FirstAsync(m => m.CharacterId == departed);
            leave.GuildMembers.Remove(member);
            await leave.SaveChangesAsync();
        }

        var result = await ImportAsync(scenario.TenantA, guild.GuildId);

        // The judgement call, asserted: the entry is NOT deleted. A gquit is very often an alt parked
        // elsewhere or a member on a break, and the rank, note and alt link on that row are the
        // community's own data — destroying them on the strength of a fact about the game would be
        // silent damage the officer never asked for.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Equal(2, await verify.RosterEntries.CountAsync());
        Assert.True(await verify.RosterEntries.AnyAsync(e => e.CharacterId == departed));

        // Reported rather than acted on, so the officer can decide.
        Assert.Equal(1, result.NotInAnyLinkedGuildCount);
        var reported = Assert.Single(result.NotInAnyLinkedGuild);
        Assert.Equal(departed, await CharacterIdOfAsync(scenario.TenantA.Tenant.Id, reported.RosterEntryId));
    }

    [Fact]
    public async Task ImportingAGuildThisCommunityHasNotLinked_Is404()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 2);

        // Linked by A only. B knows the guild id — it is global, so guessing it is not the leak — but
        // must not be able to populate its roster from a guild it does not follow.
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/roster/import",
            new ImportGuildRosterViewModel { GuildId = guild.GuildId });

        // 404, not 403: a 403 would confirm the guild is linked somewhere (tenancy.md).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);
        Assert.Empty(await verify.RosterEntries.ToListAsync());
    }

    [Fact]
    public async Task AMemberCannotImport()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture, roleA: TenantRole.Member);
        var guild = await SeedGuildWithMembersAsync(memberCount: 2);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/roster/import",
            new ImportGuildRosterViewModel { GuildId = guild.GuildId });

        // Officer, not Owner — but a plain member is still refused, the same as every other roster
        // write.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ImportingSpendsNoSyncBudget()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 4);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        var before = await GetBudgetAsync(scenario.TenantA);

        await ImportAsync(scenario.TenantA, guild.GuildId);

        var after = await GetBudgetAsync(scenario.TenantA);

        // The import reaches Blizzard for nothing: the members it reads were persisted by the guild
        // sync the link already paid for. Since every Blizzard-reaching path charges the budget before
        // the call leaves (external.md), an unchanged budget is also the assertion that no call was
        // made — including the fan-out-per-member that 6.6b exists to forbid.
        Assert.Equal(before.CallsConsumed, after.CallsConsumed);
        Assert.Equal(before.CallsRemaining, after.CallsRemaining);
    }

    [Fact]
    public async Task ImportWritesOneAuditRow_NotOnePerCharacter()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 5);
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);

        await ImportAsync(scenario.TenantA, guild.GuildId);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var audit = Assert.Single(
            await verify.AuditLogs.Where(row => row.Action == AuditAction.RosterImportedFromGuild).ToListAsync());

        Assert.Equal(scenario.TenantA.UserId, audit.ActorUserId);
        // An import acts on the roster as a whole; there is no one person it was done to.
        Assert.Null(audit.SubjectUserId);
        Assert.Equal(nameof(TenantGuild), audit.TargetType);
        Assert.Equal(guild.GuildId, audit.TargetId);

        // A second import adds nobody, so it did not happen and writes no row — the same call
        // detaching an already-detached alt makes.
        await ImportAsync(scenario.TenantA, guild.GuildId);

        await using var again = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Single(
            await again.AuditLogs.Where(row => row.Action == AuditAction.RosterImportedFromGuild).ToListAsync());
    }

    // ---- the two-tenant test, on the SAME guild ----

    [Fact]
    public async Task TwoCommunitiesImportingTheSameGuild_NeitherImportTouchesTheOthersRows()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 3);

        // Both follow it. This is normal — Guild and GuildMember are global precisely so two
        // communities can share one roster without doubling the Blizzard calls behind it.
        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);
        await LinkAsync(scenario.TenantB.Tenant.Id, guild.GuildId);

        var aImport = await ImportAsync(scenario.TenantA, guild.GuildId);
        Assert.Equal(3, aImport.Imported);

        // B's roster is still empty. If RosterEntry's query filter were missing, the diff would see A's
        // rows as "already on roster" and B would import nobody — which is what this asserts against.
        await using (var bBefore = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id))
        {
            Assert.Empty(await bBefore.RosterEntries.ToListAsync());
        }

        // A ranks and annotates its rows, then B imports the same guild.
        var aRankId = await SeedRankAsync(scenario.TenantA.Tenant.Id, "Raider", 10, "#cba76a");

        await using (var edit = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            foreach (var entry in await edit.RosterEntries.ToListAsync())
            {
                entry.TenantRankId = aRankId;
                entry.OfficerNote = "A's note.";
            }

            await edit.SaveChangesAsync();
        }

        var bImport = await ImportAsync(scenario.TenantB, guild.GuildId);

        // B gets its own three rows, and sees none of A's — the character ids are shared because the
        // characters are global, the entries are not.
        Assert.Equal(3, bImport.Imported);
        Assert.Equal(0, bImport.AlreadyOnRoster);

        await using var aVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        await using var bVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);

        var aEntries = await aVerify.RosterEntries.ToListAsync();
        var bEntries = await bVerify.RosterEntries.ToListAsync();

        Assert.Equal(3, aEntries.Count);
        Assert.Equal(3, bEntries.Count);

        // Same characters, different rows.
        Assert.Equal(aEntries.Select(e => e.CharacterId).Order(), bEntries.Select(e => e.CharacterId).Order());
        Assert.Empty(aEntries.Select(e => e.Id).Intersect(bEntries.Select(e => e.Id)));

        // A's rank and note survived B's import untouched...
        Assert.All(aEntries, entry =>
        {
            Assert.Equal(aRankId, entry.TenantRankId);
            Assert.Equal("A's note.", entry.OfficerNote);
        });

        // ...and B's rows carry neither. A rank id belonging to A must never appear on a row of B's,
        // and an officer note is one community's private remark about a person.
        Assert.All(bEntries, entry =>
        {
            Assert.Null(entry.TenantRankId);
            Assert.Null(entry.OfficerNote);
        });

        // Through the endpoint, not just the table: neither roster read shows the other's entries.
        var aRoster = await ListRosterAsync(scenario.TenantA);
        var bRoster = await ListRosterAsync(scenario.TenantB);

        Assert.Equal(3, aRoster.Count);
        Assert.Equal(3, bRoster.Count);
        Assert.Empty(aRoster.Select(r => r.Id).Intersect(bRoster.Select(r => r.Id)));
        Assert.All(bRoster, row => Assert.Null(row.RankName));
    }

    [Fact]
    public async Task OneCommunitysUnlinkedGuild_DoesNotMakeTheOthersMembersLookUnaffiliated()
    {
        // NotInAnyLinkedGuild is computed through TenantGuilds, which is query-filtered. If it read the
        // global Guilds table instead, A unlinking would have no effect here and the bug would be
        // invisible; if it ignored the filter, B's link would mask A's departed members.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var guild = await SeedGuildWithMembersAsync(memberCount: 2);

        await LinkAsync(scenario.TenantA.Tenant.Id, guild.GuildId);
        await LinkAsync(scenario.TenantB.Tenant.Id, guild.GuildId);

        await ImportAsync(scenario.TenantA, guild.GuildId);
        await ImportAsync(scenario.TenantB, guild.GuildId);

        // A walks away from the guild. Its roster rows stay — unlinking is not a roster edit.
        var unlink = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/guilds/{guild.GuildId}");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        // B still follows it, so B's members are affiliated as far as B is concerned.
        var bResult = await ImportAsync(scenario.TenantB, guild.GuildId);
        Assert.Equal(0, bResult.NotInAnyLinkedGuildCount);

        // A now follows nothing, so both of A's rows are in no guild A follows — even though the
        // characters are still in the guild, and B can still see that they are.
        await using var aVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Equal(2, await aVerify.RosterEntries.CountAsync());
    }

    // ---- seeding ----

    // A global Guild with global Characters and the GuildMember rows between them — the state the guild
    // sync (6.6b) leaves behind. Seeded directly because what is under test is the import, not how the
    // roster got into the database.
    private async Task<SeededGuild> SeedGuildWithMembersAsync(int memberCount)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var name = $"Guild{Guid.NewGuid():N}"[..18];

        var characterIds = new List<Guid>(memberCount);

        for (var i = 0; i < memberCount; i++)
        {
            var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
            characterIds.Add(character.Id);
        }

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

        for (var i = 0; i < characterIds.Count; i++)
        {
            db.GuildMembers.Add(new GuildMember
            {
                Id = Guid.NewGuid(),
                GuildId = guild.Id,
                CharacterId = characterIds[i],
                // Deliberately non-zero and varied: a row that borrowed the game's rank into
                // TenantRankId would be visible, and a rank of 0 everywhere would hide it.
                BlizzardRank = i + 1,
            });
        }

        await db.SaveChangesAsync();

        return new SeededGuild(guild.Id, characterIds);
    }

    private sealed record SeededGuild(Guid GuildId, IReadOnlyList<Guid> CharacterIds);

    // Linked directly rather than through the endpoint, because linking via HTTP calls Blizzard — which
    // is not configured in tests. Same reasoning as GuildLinkIsolationTests.
    private async Task LinkAsync(Guid tenantId, Guid guildId)
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

    private async Task<Guid> SeedRankAsync(Guid tenantId, string name, int sortOrder, string colour)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var rank = new TenantRank
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = sortOrder,
            Colour = colour,
        };

        db.TenantRanks.Add(rank);
        await db.SaveChangesAsync();

        return rank.Id;
    }

    private async Task<Guid> CharacterIdOfAsync(Guid tenantId, Guid rosterEntryId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        return (await db.RosterEntries.FirstAsync(e => e.Id == rosterEntryId)).CharacterId;
    }

    // ---- the endpoint ----

    private async Task<RosterImportServiceModel> ImportAsync(TenantSide side, Guid guildId)
    {
        var response = await side.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{side.Tenant.Slug}/roster/import",
            new ImportGuildRosterViewModel { GuildId = guildId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RosterImportServiceModel>(Json))!;
    }

    private async Task<SyncBudgetServiceModel> GetBudgetAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/sync/budget");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SyncBudgetServiceModel>(Json))!;
    }

    private async Task<IReadOnlyList<RosterEntryServiceModel>> ListRosterAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/roster");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<RosterEntryServiceModel>>(Json);

        return page!.Items;
    }
}
