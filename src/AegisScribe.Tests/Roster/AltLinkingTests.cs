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

// Alt linking through the endpoint. Two things are under test and they are easy to conflate: the SHAPE
// rules that keep alts one level deep, and the AUTHORIZATION rule that decides whose characters you may
// reorganise at all.
//
// There is no cycle-detection walk to test, because there is no cycle to detect — rules 3 and 4 refuse
// the only thing a cycle could be built from.
[Collection("AegisScribe API")]
public class AltLinkingTests(AegisScribeAppFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task AMemberCanLinkTwoCharactersTheyClaim()
    {
        var member = await TenantSide.CreateAsync(fixture, "Alt Link", TenantRole.Member);
        var main = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);
        var alt = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(member, alt, main)).StatusCode);

        var roster = await ListAsync(member);
        Assert.Equal(main, Assert.Single(roster, r => r.Id == alt).MainRosterEntryId);
        // The main stays a main. One level deep.
        Assert.Null(Assert.Single(roster, r => r.Id == main).MainRosterEntryId);
    }

    [Fact]
    public async Task LinkingACharacterToItself_Is400OnTheField()
    {
        // Rule 1. A field error rather than a conflict — the form highlights the character picker.
        // The validator cannot catch it: the entry is on the route, the target in the body.
        var member = await TenantSide.CreateAsync(fixture, "Alt Self", TenantRole.Member);
        var entry = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);

        var response = await LinkAsync(member, entry, entry);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(LinkAltViewModel.MainRosterEntryId), problem!.Errors.Keys);
    }

    [Fact]
    public async Task LinkingToACharacterThatIsItselfAnAlt_Is409()
    {
        // Rule 3 — depth 2 from below. Together with rule 4 this is what forecloses cycles: a 2-cycle
        // would need B to be an alt of A while A is an alt of B, and this refusal is the half that
        // blocks the second link.
        var member = await TenantSide.CreateAsync(fixture, "Alt Depth Below", TenantRole.Member);
        var main = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);
        var alt = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);
        var third = await SeedEntryAsync(member.Tenant.Id, "Thornclaw", claimedBy: member.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(member, alt, main)).StatusCode);

        var response = await LinkAsync(member, third, alt);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/alt-depth-exceeded", problem!.Type);
    }

    [Fact]
    public async Task LinkingACharacterThatAlreadyHasAlts_Is409()
    {
        // Rule 4 — depth 2 from above, and the other half of the cycle argument.
        var member = await TenantSide.CreateAsync(fixture, "Alt Depth Above", TenantRole.Member);
        var main = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);
        var alt = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);
        var other = await SeedEntryAsync(member.Tenant.Id, "Thornclaw", claimedBy: member.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(member, alt, main)).StatusCode);

        // main now has an alt, so it may not become one itself.
        var response = await LinkAsync(member, main, other);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ATwoCycleIsUnreachable()
    {
        // Its own case because the reason it passes is worth pinning: the SECOND link is refused by
        // rule 3 (A is already an alt), not by any cycle check. There is no walk here to regress.
        var member = await TenantSide.CreateAsync(fixture, "Alt Cycle", TenantRole.Member);
        var a = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);
        var b = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(member, a, b)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await LinkAsync(member, b, a)).StatusCode);

        var roster = await ListAsync(member);
        Assert.Equal(b, Assert.Single(roster, r => r.Id == a).MainRosterEntryId);
        Assert.Null(Assert.Single(roster, r => r.Id == b).MainRosterEntryId);
    }

    [Fact]
    public async Task TwoOppositeLinksRacing_CannotProduceACycle()
    {
        // What ATwoCycleIsUnreachable above does NOT cover, because it awaits the first response before
        // sending the second. Fired together, both requests read pre-commit state, both pass the depth
        // rules, and both write a DIFFERENT row — so nothing at the database level detects a conflict
        // unless the check and the write are the same statement. A read-then-write implementation
        // passes this occasionally and fails it under load, so the assertion is on the exact outcome.
        var member = await TenantSide.CreateAsync(fixture, "Alt Race", TenantRole.Member);
        var a = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);
        var b = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);

        var responses = await Task.WhenAll(
            LinkAsync(member, a, b),
            LinkAsync(member, b, a));

        // Exactly one may win. Which one is genuinely undecided and must not be asserted.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));

        var roster = await ListAsync(member);
        var entries = roster.Where(r => r.Id == a || r.Id == b).ToList();

        // One main, one alt — never two alts pointing at each other, which is the cycle.
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, r => r.MainRosterEntryId is null);
        Assert.Single(entries, r => r.MainRosterEntryId is not null);

        // And the alt points at the one that stayed a main, rather than at something that is itself an
        // alt — the invariant stated positively.
        var alt = entries.Single(r => r.MainRosterEntryId is not null);
        var main = entries.Single(r => r.MainRosterEntryId is null);
        Assert.Equal(main.Id, alt.MainRosterEntryId);
    }

    [Fact]
    public async Task AMemberClaimingOnlyOneEnd_Is403()
    {
        // Linking asserts a relationship BETWEEN two characters, so owning one end is not enough.
        var member = await TenantSide.CreateAsync(fixture, "Alt One End", TenantRole.Member);
        var somebodyElse = await TenantSide.JoinAsync(fixture, member.Tenant, TenantRole.Member);

        var mine = await SeedEntryAsync(member.Tenant.Id, "Thornbite", claimedBy: member.UserId);
        var theirs = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: somebodyElse.UserId);

        Assert.Equal(HttpStatusCode.Forbidden, (await LinkAsync(member, mine, theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LinkAsync(member, theirs, mine)).StatusCode);
    }

    [Fact]
    public async Task AnOfficerCanLinkCharactersTheyDoNotClaim_AndItIsAudited()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Alt Officer", TenantRole.Officer);
        var owner = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);

        var main = await SeedEntryAsync(officer.Tenant.Id, "Thornwake", claimedBy: owner.UserId);
        var alt = await SeedEntryAsync(officer.Tenant.Id, "Thornbite", claimedBy: owner.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(officer, alt, main)).StatusCode);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        var entry = Assert.Single(db.AuditLogs.Where(a => a.TargetId == alt).ToList());

        Assert.Equal(AuditAction.RosterEntryAltLinked, entry.Action);
        Assert.Equal(officer.UserId, entry.ActorUserId);
        // Whose character was reorganised — the half that makes the row worth keeping.
        Assert.Equal(owner.UserId, entry.SubjectUserId);
        Assert.Equal("No main", entry.Before);
        Assert.Equal($"Alt of {main}", entry.After);
    }

    [Fact]
    public async Task AnOfficerReorganisingTheirOwnCharacters_WritesNoAuditRow()
    {
        // The audit rule is "the actor does not claim the entry", not "the actor is an officer". An
        // officer tidying their own alts is nobody else's business.
        var officer = await TenantSide.CreateAsync(fixture, "Alt Officer Own", TenantRole.Officer);
        var main = await SeedEntryAsync(officer.Tenant.Id, "Thornwake", claimedBy: officer.UserId);
        var alt = await SeedEntryAsync(officer.Tenant.Id, "Thornbite", claimedBy: officer.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(officer, alt, main)).StatusCode);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        Assert.Empty(db.AuditLogs.Where(a => a.TargetId == alt).ToList());
    }

    [Fact]
    public async Task TheAltsClaimantCanUnlink_EvenWhenAnOfficerMadeTheLink()
    {
        // Unlink deliberately checks only the entry being detached, not both ends. Otherwise a member
        // whose character an officer attached to somebody else's main would be trapped in it.
        var officer = await TenantSide.CreateAsync(fixture, "Alt Unlink", TenantRole.Officer);
        var owner = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);
        var somebodyElse = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);

        var main = await SeedEntryAsync(officer.Tenant.Id, "Thornwake", claimedBy: somebodyElse.UserId);
        var alt = await SeedEntryAsync(officer.Tenant.Id, "Thornbite", claimedBy: owner.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await LinkAsync(officer, alt, main)).StatusCode);

        var response = await owner.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{owner.Tenant.Slug}/roster/{alt}/main");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(Assert.Single(await ListAsync(owner), r => r.Id == alt).MainRosterEntryId);
    }

    [Fact]
    public async Task UnlinkingAnEntryWithNoMain_Is204()
    {
        var member = await TenantSide.CreateAsync(fixture, "Alt Unlink Empty", TenantRole.Member);
        var entry = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{member.Tenant.Slug}/roster/{entry}/main");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AMemberCannotUnlinkACharacterTheyDoNotClaim()
    {
        var member = await TenantSide.CreateAsync(fixture, "Alt Unlink Theirs", TenantRole.Member);
        var somebodyElse = await TenantSide.JoinAsync(fixture, member.Tenant, TenantRole.Member);
        var theirs = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: somebodyElse.UserId);

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{member.Tenant.Slug}/roster/{theirs}/main");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LinkingToARosterEntryThatDoesNotExist_Is404()
    {
        var member = await TenantSide.CreateAsync(fixture, "Alt Unknown", TenantRole.Member);
        var entry = await SeedEntryAsync(member.Tenant.Id, "Thornwake", claimedBy: member.UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await LinkAsync(member, entry, Guid.NewGuid())).StatusCode);
    }

    private static Task<HttpResponseMessage> LinkAsync(TenantSide side, Guid rosterEntryId, Guid mainRosterEntryId) =>
        side.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{side.Tenant.Slug}/roster/{rosterEntryId}/main",
            new LinkAltViewModel { MainRosterEntryId = mainRosterEntryId });

    private static async Task<IReadOnlyList<RosterEntryServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/roster?limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<RosterEntryServiceModel>>(Json))!.Items;
    }

    // A roster entry plus the claim that decides who may reorganise it, both seeded directly — the
    // claim endpoint has its own tests.
    private async Task<Guid> SeedEntryAsync(Guid tenantId, string characterName, string claimedBy)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(
            fixture, realm.Id, $"{characterName}{Guid.NewGuid():N}"[..12]);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        db.RosterEntries.Add(entry);
        db.CharacterClaims.Add(new CharacterClaim
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            UserId = claimedBy,
            ClaimedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        return entry.Id;
    }
}
