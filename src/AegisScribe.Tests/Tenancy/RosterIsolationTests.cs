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

// 7.2's two-tenant test, and the sharpest version of the two-zone rule in the repo so far: every case
// below seeds ONE global Character and has BOTH communities roster it.
//
// That is the shape tenancy.md builds up to — one Character row, N RosterEntry rows — and it is the
// configuration a broken query filter cannot survive. A filter that leaked would not show B some
// unrelated row; it would show B the rank and standing A assigned to a character B also knows, which
// is both the likeliest real-world arrangement and the one most likely to look plausible on screen.
[Collection("AegisScribe API")]
public class RosterIsolationTests(AegisScribeAppFixture fixture)
{
    // The API serializes enums as their NAME (api-contract.md), so a default deserializer cannot read
    // RosterEntryServiceModel.Class.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task TwoCommunitiesRosteringTheSameCharacter_EachSeeOnlyTheirOwnEntry()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();
        var onlyForB = await SeedCharacterAsync();

        var aRank = await SeedRankAsync(scenario.TenantA.Tenant.Id, "Raider", 10, "#cba76a");
        var bRank = await SeedRankAsync(scenario.TenantB.Tenant.Id, "Trial", 20, "#616d7e");

        var aEntry = await SeedEntryAsync(scenario.TenantA.Tenant.Id, shared.Id, aRank);
        await SeedEntryAsync(scenario.TenantB.Tenant.Id, shared.Id, bRank);
        await SeedEntryAsync(scenario.TenantB.Tenant.Id, onlyForB.Id, null);

        var aRoster = await ListAsync(scenario.TenantA);
        var bRoster = await ListAsync(scenario.TenantB);

        // A sees one row: its own entry for the shared character, at ITS rank.
        var aRow = Assert.Single(aRoster);
        Assert.Equal(aEntry, aRow.Id);
        Assert.Equal(shared.Id, aRow.CharacterId);
        Assert.Equal("Raider", aRow.RankName);
        Assert.Equal("#cba76a", aRow.RankColour);

        // B sees two, and the one for the SAME character carries B's own rank — the two communities
        // disagree about this character and both are right, which is the entire reason RosterEntry is
        // tenant-scoped while Character is global.
        Assert.Equal(2, bRoster.Count);
        var bRow = Assert.Single(bRoster, row => row.CharacterId == shared.Id);
        Assert.NotEqual(aEntry, bRow.Id);
        Assert.Equal("Trial", bRow.RankName);

        // And neither sees the other's entry id, nor B's second character.
        Assert.DoesNotContain(aRoster, row => row.Id == bRow.Id);
        Assert.DoesNotContain(aRoster, row => row.CharacterId == onlyForB.Id);
    }

    [Fact]
    public async Task ReachingAnotherCommunitysRosterRoute_Is404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var character = await SeedCharacterAsync();
        await SeedEntryAsync(scenario.TenantA.Tenant.Id, character.Id, null);

        // 404, never 403 — a 403 would confirm the community exists (tenancy.md).
        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/roster");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ARankHeldInOneCommunity_DoesNotProtectTheSameRankIdFromAnother()
    {
        // The holder count added in 7.2 is query-filtered, and this is what that buys: A's roster
        // entries must not make B's delete of a rank id 409, and B's delete must still reach nothing
        // of A's. Both halves matter — the first would be a denial of service across tenants, the
        // second a cross-tenant delete.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var character = await SeedCharacterAsync();
        var aRank = await SeedRankAsync(scenario.TenantA.Tenant.Id, "Raider", 10, "#cba76a");
        await SeedEntryAsync(scenario.TenantA.Tenant.Id, character.Id, aRank);

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/ranks/{aRank}");

        // No holders in B, so no 409 — and the delete matches no rows in B either.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var aRow = Assert.Single(await ListAsync(scenario.TenantA));
        Assert.Equal("Raider", aRow.RankName);
    }

    [Fact]
    public async Task ACommunityCannotNameAnotherCommunitysEntryAsAMain()
    {
        // 7.3's cross-tenant case. B knows A's roster entry id — guessing a GUID is not the leak.
        // What B must not be able to do is reach across with it, and the refusal is a 404 by the same
        // mechanism as every other cross-tenant id here: the query filter makes A's row come back null
        // in B's request, indistinguishable from nonexistent.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var characterInA = await SeedCharacterAsync();
        var characterInB = await SeedCharacterAsync();

        var entryInA = await SeedEntryAsync(scenario.TenantA.Tenant.Id, characterInA.Id, null);
        var entryInB = await SeedEntryAsync(scenario.TenantB.Tenant.Id, characterInB.Id, null);

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/roster/{entryInB}/main",
            new LinkAltViewModel { MainRosterEntryId = entryInA });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And nothing was written on either side.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);
        Assert.False(await verify.RosterEntries.AnyAsync(e => e.Id == entryInB && e.MainRosterEntryId != null));
    }

    [Fact]
    public async Task AnOfficersWriteCannotReachAnotherCommunitysEntry()
    {
        // tenancy.md's cross-tenant write assertion, now that 7.4 has given the roster writes at all.
        // B is an owner in B and has no standing in A whatsoever; every one of these is a 404, because
        // the query filter makes A's row indistinguishable from nonexistent rather than forbidden.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var character = await SeedCharacterAsync();
        var entryInA = await SeedEntryAsync(scenario.TenantA.Tenant.Id, character.Id, null);

        var route = $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/roster/{entryInA}";

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await scenario.TenantB.SendAsync(
                HttpMethod.Put, $"{route}/rank", new SetRosterRankViewModel())).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await scenario.TenantB.SendAsync(
                HttpMethod.Put, $"{route}/note", new SetOfficerNoteViewModel { OfficerNote = "mine now" })).StatusCode);

        // The delete is the exception that proves the rule: DELETE is idempotent, so "not this
        // community's" is indistinguishable from "already gone" and both are 204. What matters is that
        // A's row survives it.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await scenario.TenantB.SendAsync(HttpMethod.Delete, route)).StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var survivor = Assert.Single(await verify.RosterEntries.Where(e => e.Id == entryInA).ToListAsync());
        Assert.Null(survivor.OfficerNote);
    }

    [Fact]
    public async Task AddingWithAnotherCommunitysRank_IsRefusedRatherThanSilentlyUnranked()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var character = await SeedCharacterAsync();

        await using var seed = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var rankInA = new TenantRank { Id = Guid.NewGuid(), Name = "Raider", SortOrder = 10, Colour = "#cba76a" };
        seed.TenantRanks.Add(rankInA);
        await seed.SaveChangesAsync();

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/roster",
            new AddRosterEntryViewModel { CharacterId = character.Id, TenantRankId = rankInA.Id });

        // Refused, not quietly accepted-and-unranked: a roster row showing no rank because the id was
        // silently dropped is the kind of wrong that nobody reports.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A global Character on its own global Realm. Both zones are seeded directly, the way every other
    // test in this suite reaches reference data.
    private async Task<Domain.Managers.Models.Domain.Character> SeedCharacterAsync()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        return await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
    }

    // Seeds directly, because the write endpoints are 7.4's. What is under test here is isolation of
    // the entry, not how one is created — the same reasoning GuildLinkIsolationTests seeds links by.
    private async Task<Guid> SeedEntryAsync(Guid tenantId, Guid characterId, Guid? tenantRankId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = characterId,
            TenantRankId = tenantRankId,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        db.RosterEntries.Add(entry);
        await db.SaveChangesAsync();

        return entry.Id;
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

    private static async Task<IReadOnlyList<RosterEntryServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/roster");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<RosterEntryServiceModel>>(Json);

        return page!.Items;
    }
}
