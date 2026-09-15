using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using AegisScribe.Tests.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.Tests.Roster;

// 7.4's writes, through the endpoint: add, rank, note, remove. Officer-gated, and each audited exactly
// when the actor does not claim the entry — which is the prompt's "every officer write to someone
// else's entry" without also auditing an officer tidying their own characters.
[Collection("AegisScribe API")]
public class RosterWriteTests(AegisScribeAppFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task AnOfficerCanAddACharacterToTheRoster()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Add", TenantRole.Officer);
        var character = await SeedCharacterAsync("Aldric");

        var response = await officer.SendAsync(
            HttpMethod.Post,
            RosterRoute(officer),
            new AddRosterEntryViewModel { CharacterId = character.Id });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var row = Assert.Single(await ListAsync(officer));
        Assert.Equal(character.Id, row.CharacterId);
        Assert.Null(row.RankId);
    }

    [Fact]
    public async Task AddingTheSameCharacterTwice_Is409WithTheStableTypeUri()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Add Twice", TenantRole.Officer);
        var character = await SeedCharacterAsync("Aldric");
        var body = new AddRosterEntryViewModel { CharacterId = character.Id };

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(HttpMethod.Post, RosterRoute(officer), body)).StatusCode);

        var duplicate = await officer.SendAsync(HttpMethod.Post, RosterRoute(officer), body);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // A different `type` from character-already-claimed: being ON the roster and being CLAIMED are
        // different facts, and a client branches on which.
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/character-already-on-roster", problem!.Type);
    }

    [Fact]
    public async Task TwoOfficersAddingTheSameCharacterAtOnce_Is409NotA500()
    {
        // The add is a read-then-write: both requests can pass the is-on-roster check before either
        // commits. The unique index is what stops both landing — but only if the violation is
        // TRANSLATED. Untranslated it surfaces as a raw DbUpdateException, and the loser of a race
        // gets a 500 for a condition the client already knows how to handle.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Add Race", TenantRole.Officer);
        var character = await SeedCharacterAsync("Aldric");
        var body = new AddRosterEntryViewModel { CharacterId = character.Id };

        var responses = await Task.WhenAll(
            officer.SendAsync(HttpMethod.Post, RosterRoute(officer), body),
            officer.SendAsync(HttpMethod.Post, RosterRoute(officer), body));

        // Exactly one wins, and the loser is refused rather than crashing. Whichever way the race
        // lands, no 500.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
        Assert.Single(await ListAsync(officer));
    }

    [Fact]
    public async Task RemovingWhileAnAltIsLinkedToIt_IsNeverA500()
    {
        // The has-alts check is the same shape: counted, then deleted. An alt linked in between makes
        // the Restrict foreign key refuse the delete, and that refusal has to reach the client as the
        // 409 it means rather than as a crash.
        //
        // Which side wins is genuinely undecided, so the assertion is on what must never happen.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Remove Race", TenantRole.Officer);
        var main = await SeedEntryAsync(officer.Tenant.Id, "Thornwake", claimedBy: officer.UserId);
        var alt = await SeedEntryAsync(officer.Tenant.Id, "Thornbite", claimedBy: officer.UserId);

        var responses = await Task.WhenAll(
            officer.SendAsync(HttpMethod.Delete, $"{RosterRoute(officer)}/{main}"),
            officer.SendAsync(
                HttpMethod.Put,
                $"{RosterRoute(officer)}/{alt}/main",
                new LinkAltViewModel { MainRosterEntryId = main }));

        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
    }

    [Fact]
    public async Task AddingACharacterThatDoesNotExist_Is404()
    {
        // Without the existence check the foreign key would make this a 500.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Add Unknown", TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Post,
            RosterRoute(officer),
            new AddRosterEntryViewModel { CharacterId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOfficerCanSetAndClearARank()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Rank", TenantRole.Officer);
        var rank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric");

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(
                HttpMethod.Put,
                $"{RosterRoute(officer)}/{entry}/rank",
                new SetRosterRankViewModel { TenantRankId = rank })).StatusCode);

        Assert.Equal("Raider", Assert.Single(await ListAsync(officer)).RankName);

        // Null clears — the one thing a PUT of a single field can say unambiguously, and the reason
        // rank and note are separate routes rather than one PATCH.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(
                HttpMethod.Put,
                $"{RosterRoute(officer)}/{entry}/rank",
                new SetRosterRankViewModel { TenantRankId = null })).StatusCode);

        Assert.Null(Assert.Single(await ListAsync(officer)).RankName);
    }

    [Fact]
    public async Task SettingARankFromAnotherCommunity_Is400RatherThanSilentlyUnranked()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Rank Foreign", TenantRole.Officer);
        var elsewhere = await TenantSeeding.CreateTenantAsync(fixture);
        var foreignRank = await SeedRankAsync(elsewhere.Id, "Raider", 10);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric");

        var response = await officer.SendAsync(
            HttpMethod.Put,
            $"{RosterRoute(officer)}/{entry}/rank",
            new SetRosterRankViewModel { TenantRankId = foreignRank });

        // Refused, not stored and not quietly cleared — either would leave the roster showing
        // something nobody chose.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(Assert.Single(await ListAsync(officer)).RankName);
    }

    [Fact]
    public async Task SettingANoteDoesNotDisturbTheRank()
    {
        // The reason rank and note are separate PUTs: a combined PATCH from a UI that edits only one
        // of them cannot tell "omitted" from "null", and would wipe the other.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Note", TenantRole.Officer);
        var rank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric");

        await officer.SendAsync(
            HttpMethod.Put,
            $"{RosterRoute(officer)}/{entry}/rank",
            new SetRosterRankViewModel { TenantRankId = rank });

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(
                HttpMethod.Put,
                $"{RosterRoute(officer)}/{entry}/note",
                new SetOfficerNoteViewModel { OfficerNote = "Tanking Tuesdays" })).StatusCode);

        var row = Assert.Single(await ListAsync(officer));
        Assert.Equal("Tanking Tuesdays", row.OfficerNote);
        Assert.Equal("Raider", row.RankName);
    }

    [Fact]
    public async Task AnOfficerCanRemoveAnEntry_AndRemovingItAgainIs204()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Remove", TenantRole.Officer);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric");

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(HttpMethod.Delete, $"{RosterRoute(officer)}/{entry}")).StatusCode);

        Assert.Empty(await ListAsync(officer));

        // DELETE is idempotent — a client that retries after a dropped response must not see its own
        // success as an error.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await officer.SendAsync(HttpMethod.Delete, $"{RosterRoute(officer)}/{entry}")).StatusCode);
    }

    [Fact]
    public async Task RemovingAnEntryThatHasAlts_Is409AndLeavesEverythingAlone()
    {
        // Refused rather than cascading: detaching somebody's other characters as a side effect of
        // removing one is the silent kind of damage, and the Restrict FK would otherwise make it a 500.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Remove Main", TenantRole.Officer);
        var main = await SeedEntryAsync(officer.Tenant.Id, "Thornwake");
        await SeedEntryAsync(officer.Tenant.Id, "Thornbite", mainRosterEntryId: main);

        var response = await officer.SendAsync(HttpMethod.Delete, $"{RosterRoute(officer)}/{main}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/roster-entry-has-alts", problem!.Type);

        Assert.Equal(2, (await ListAsync(officer)).Count);
    }

    [Fact]
    public async Task EveryOfficerWriteToSomebodyElsesEntry_IsAudited()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Audit", TenantRole.Officer);
        var owner = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);
        var rank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric", claimedBy: owner.UserId);

        await officer.SendAsync(
            HttpMethod.Put, $"{RosterRoute(officer)}/{entry}/rank", new SetRosterRankViewModel { TenantRankId = rank });
        await officer.SendAsync(
            HttpMethod.Put, $"{RosterRoute(officer)}/{entry}/note", new SetOfficerNoteViewModel { OfficerNote = "Solid" });

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        var rows = db.AuditLogs.Where(a => a.TargetId == entry).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Action == AuditAction.RosterEntryRankChanged);
        Assert.Contains(rows, r => r.Action == AuditAction.RosterEntryNoteChanged);
        Assert.All(rows, r => Assert.Equal(officer.UserId, r.ActorUserId));
        // Whose standing was changed — the half that makes the row worth keeping.
        Assert.All(rows, r => Assert.Equal(owner.UserId, r.SubjectUserId));

        // The note's CONTENT is not copied into the audit trail. It is one person's private remark
        // about another, and 14.2 will make audit rows readable by every officer — a second permanent
        // copy of it is not something this row should create.
        Assert.DoesNotContain(rows, r => (r.Before + r.After).Contains("Solid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOfficerManagingTheirOwnCharacter_WritesNoAuditRow()
    {
        // The rule is "the actor does not claim the entry", not "the actor is an officer".
        var officer = await TenantSide.CreateAsync(fixture, "Roster Audit Own", TenantRole.Officer);
        var rank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric", claimedBy: officer.UserId);

        await officer.SendAsync(
            HttpMethod.Put, $"{RosterRoute(officer)}/{entry}/rank", new SetRosterRankViewModel { TenantRankId = rank });

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        Assert.Empty(db.AuditLogs.Where(a => a.TargetId == entry).ToList());
    }

    [Fact]
    public async Task AMemberCannotWriteToTheRosterAtAll()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Member Writes", TenantRole.Member);
        var entry = await SeedEntryAsync(member.Tenant.Id, "Aldric");
        var character = await SeedCharacterAsync("Borin");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.SendAsync(
                HttpMethod.Post, RosterRoute(member), new AddRosterEntryViewModel { CharacterId = character.Id })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.SendAsync(
                HttpMethod.Put, $"{RosterRoute(member)}/{entry}/rank", new SetRosterRankViewModel())).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.SendAsync(
                HttpMethod.Put, $"{RosterRoute(member)}/{entry}/note", new SetOfficerNoteViewModel())).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.SendAsync(HttpMethod.Delete, $"{RosterRoute(member)}/{entry}")).StatusCode);
    }

    [Fact]
    public async Task WithoutATokenEveryWriteIs401()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Roster Anonymous Writes", TenantRole.Officer);
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Aldric");
        var route = RosterRoute(officer);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.PostAsJsonAsync(route, new AddRosterEntryViewModel { CharacterId = Guid.NewGuid() })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.PutAsJsonAsync($"{route}/{entry}/rank", new SetRosterRankViewModel())).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.DeleteAsync($"{route}/{entry}")).StatusCode);
    }

    private static string RosterRoute(TenantSide side) => $"/api/v1/t/{side.Tenant.Slug}/roster";

    private static async Task<IReadOnlyList<RosterEntryServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"{RosterRoute(side)}?limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<RosterEntryServiceModel>>(Json))!.Items;
    }

    private async Task<Domain.Managers.Models.Domain.Character> SeedCharacterAsync(string name)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        return await CharacterSeeding.CreateCharacterAsync(
            fixture, realm.Id, $"{name}{Guid.NewGuid():N}"[..12]);
    }

    private async Task<Guid> SeedRankAsync(Guid tenantId, string name, int sortOrder)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var rank = new TenantRank { Id = Guid.NewGuid(), Name = name, SortOrder = sortOrder, Colour = "#cba76a" };
        db.TenantRanks.Add(rank);
        await db.SaveChangesAsync();

        return rank.Id;
    }

    private async Task<Guid> SeedEntryAsync(
        Guid tenantId, string characterName, Guid? mainRosterEntryId = null, string? claimedBy = null)
    {
        var character = await SeedCharacterAsync(characterName);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            MainRosterEntryId = mainRosterEntryId,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        db.RosterEntries.Add(entry);

        if (claimedBy is not null)
        {
            db.CharacterClaims.Add(new CharacterClaim
            {
                Id = Guid.NewGuid(),
                CharacterId = character.Id,
                UserId = claimedBy,
                ClaimedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        return entry.Id;
    }
}
