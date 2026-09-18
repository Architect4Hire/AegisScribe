using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 8.3 — the paths into and out of a community, and every refusal along them.
//
// The refusals are the subject. auth.md states four rules that all reduce to two predicates (an actor
// acts only on somebody below their own rank, and grants only a role at or below it) plus one
// invariant that outranks both: a community always has an Owner. Each test below names which of those
// it is pinning.
[Collection("AegisScribe API")]
public class MembershipLifecycleTests(AegisScribeAppFixture fixture)
{
    // The API serializes enums as their NAME (api-contract.md), so a default deserializer cannot read
    // TenantRole or InvitationStatus.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // ---- role changes ----

    [Fact]
    public async Task AnOfficerMayPromoteAMemberToOfficer()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var response = await SetRoleAsync(officer, member.UserId, TenantRole.Officer);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantA.Tenant.Id, member.UserId));
    }

    [Fact]
    public async Task AnOfficerMayNotActOnAnotherOfficer()
    {
        // The first predicate: strictly above, or an Owner. Two officers are peers, so neither may
        // demote, promote or remove the other.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var actor = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        var peer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);

        var demote = await SetRoleAsync(actor, peer.UserId, TenantRole.Member);
        Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);

        var remove = await actor.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{peer.UserId}");
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);

        // Neither refusal wrote anything.
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantA.Tenant.Id, peer.UserId));
    }

    [Fact]
    public async Task OnlyAnOwnerMayCreateAnotherOwner()
    {
        // The second predicate: you cannot hand out what you do not hold.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var refused = await SetRoleAsync(officer, member.UserId, TenantRole.Owner);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(TenantRole.Member, await RoleOfAsync(scenario.TenantA.Tenant.Id, member.UserId));

        // The community's owner can.
        var allowed = await SetRoleAsync(scenario.TenantA, member.UserId, TenantRole.Owner);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(scenario.TenantA.Tenant.Id, member.UserId));
    }

    [Fact]
    public async Task OnlyAnOwnerMayDemoteAnOfficer()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);

        var response = await SetRoleAsync(scenario.TenantA, officer.UserId, TenantRole.Member);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TenantRole.Member, await RoleOfAsync(scenario.TenantA.Tenant.Id, officer.UserId));
    }

    [Fact]
    public async Task AMemberMayNotChangeAnybodysRole()
    {
        // The route's policy, not a Business rule — and it refuses before anything is read.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        var other = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var response = await SetRoleAsync(member, other.UserId, TenantRole.Officer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangingTheRoleOfSomebodyWhoIsNotAMemberHere_Is404()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        // B's owner is a real user with a real id, and no standing whatsoever in A.
        var response = await SetRoleAsync(scenario.TenantA, scenario.TenantB.UserId, TenantRole.Officer);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- the last owner ----

    [Fact]
    public async Task TheLastOwnerCanNeitherBeDemotedNorRemoved()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var owner = scenario.TenantA;

        var demote = await SetRoleAsync(owner, owner.UserId, TenantRole.Member);
        Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);

        var remove = await owner.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{owner.Tenant.Slug}/members/{owner.UserId}");
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);

        // Still an owner, and still a member.
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(owner.Tenant.Id, owner.UserId));
    }

    [Fact]
    public async Task TheRefusalNamesTheRuleSoAScreenCanActOnIt()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var owner = scenario.TenantA;

        var response = await SetRoleAsync(owner, owner.UserId, TenantRole.Member);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Clients branch on `type`, never on prose (api-contract.md) — and this refusal is distinct
        // from "you may not do that to this member", because the fix is different.
        Assert.Equal(
            "https://api.aegisscribe.com/problems/last-owner",
            problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task OnceThereIsASecondOwner_TheFirstMayStepDown()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var owner = scenario.TenantA;
        var second = await TenantSide.JoinAsync(fixture, owner.Tenant, TenantRole.Owner);

        var response = await SetRoleAsync(owner, owner.UserId, TenantRole.Member);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TenantRole.Member, await RoleOfAsync(owner.Tenant.Id, owner.UserId));
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(owner.Tenant.Id, second.UserId));
    }

    [Fact]
    public async Task TwoOwnersDemotingEachOtherAtOnce_LeaveAtLeastOneOwnerStanding()
    {
        // The test the whole design is shaped around. A count-then-write would pass this check in both
        // requests — each reads a state with two owners — and then write DIFFERENT rows, so nothing
        // conflicts, nothing throws, and the community ends with no owner at all. The guard lives in
        // the UPDATE's own WHERE precisely so the row lock serialises them.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var first = scenario.TenantA;
        var second = await TenantSide.JoinAsync(fixture, first.Tenant, TenantRole.Owner);

        var demoteSecond = SetRoleAsync(first, second.UserId, TenantRole.Member);
        var demoteFirst = SetRoleAsync(second, first.UserId, TenantRole.Member);

        var responses = await Task.WhenAll(demoteSecond, demoteFirst);

        // Whatever the interleaving, the invariant holds.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, first.Tenant.Id);
        var owners = await verify.TenantMemberships
            .Where(m => m.TenantId == first.Tenant.Id && m.Role == TenantRole.Owner)
            .CountAsync();

        Assert.True(owners >= 1, "The community was left with no owner.");

        // And at most one of the two demotions can have succeeded.
        Assert.True(
            responses.Count(r => r.StatusCode == HttpStatusCode.NoContent) <= 1,
            "Both demotions succeeded, which would mean the last-owner guard did not hold.");
    }

    // ---- removal ----

    [Fact]
    public async Task RemovingAMemberTakesTheirClaimsAndRosterEntriesButNotTheCharacter()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var tenantId = scenario.TenantA.Tenant.Id;
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var character = await SeedCharacterAsync();
        await SeedRosterEntryAsync(tenantId, character.Id);
        await SeedClaimAsync(tenantId, character.Id, member.UserId);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        Assert.False(await verify.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == member.UserId));
        Assert.Empty(await verify.CharacterClaims.Where(c => c.UserId == member.UserId).ToListAsync());
        Assert.Empty(await verify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());

        // auth.md: "their global character data does not" go with them. The character is a fact about
        // the world and other communities may be rostering it right now.
        await using var global = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.True(await global.Characters.AnyAsync(c => c.Id == character.Id));
    }

    [Fact]
    public async Task RemovingAMemberWhoseEntryIsSomebodyElsesMain_DetachesRatherThanFailing()
    {
        // An officer can link an alt across two members, so a departing member's entry can be another
        // member's main. The alt FK is Restrict, so without the detach this is a 500.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var tenantId = scenario.TenantA.Tenant.Id;
        var leaver = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        var stayer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var leaverCharacter = await SeedCharacterAsync();
        var stayerCharacter = await SeedCharacterAsync();

        var mainEntryId = await SeedRosterEntryAsync(tenantId, leaverCharacter.Id);
        var altEntryId = await SeedRosterEntryAsync(tenantId, stayerCharacter.Id, mainRosterEntryId: mainEntryId);

        await SeedClaimAsync(tenantId, leaverCharacter.Id, leaver.UserId);
        await SeedClaimAsync(tenantId, stayerCharacter.Id, stayer.UserId);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{leaver.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        // The leaver's entry is gone; the stayer's survives as a main, because the relationship it
        // recorded no longer has two ends.
        Assert.False(await verify.RosterEntries.AnyAsync(e => e.Id == mainEntryId));
        var survivor = await verify.RosterEntries.FirstAsync(e => e.Id == altEntryId);
        Assert.Null(survivor.MainRosterEntryId);
    }

    [Fact]
    public async Task RemovingSomebodyWhoIsNotAMemberHere_Is204NotAnError()
    {
        // DELETE is idempotent (api-contract.md), and "not a member here" is indistinguishable from
        // "already removed".
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{scenario.TenantB.UserId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // And B's own membership in B is untouched.
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(scenario.TenantB.Tenant.Id, scenario.TenantB.UserId));
    }

    // ---- leaving ----

    [Fact]
    public async Task AnOfficerMayWalkOutWithoutAnybodyElsesCooperation()
    {
        // The gap this closes. An Officer outranks nobody who could remove them except an Owner, so
        // without a self-service door they are trapped in a community by the rank arithmetic.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, officer.UserId));
    }

    [Fact]
    public async Task LeavingTakesTheSamePossessionsBeingRemovedWouldHave()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var tenantId = scenario.TenantA.Tenant.Id;
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var character = await SeedCharacterAsync();
        await SeedRosterEntryAsync(tenantId, character.Id);
        await SeedClaimAsync(tenantId, character.Id, member.UserId);

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        Assert.False(await verify.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == member.UserId));
        Assert.Empty(await verify.CharacterClaims.Where(c => c.UserId == member.UserId).ToListAsync());
        Assert.Empty(await verify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());

        // The character is global and survives, exactly as it does when an officer does the removing.
        await using var global = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.True(await global.Characters.AnyAsync(c => c.Id == character.Id));
    }

    [Fact]
    public async Task TheLastOwnerMayNotWalkOutEither()
    {
        // The one rule that refuses this. Leaving is otherwise always permitted, because acting on
        // yourself needs no rank — but a community cannot be left ownerless by the back door.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var owner = scenario.TenantA;

        var response = await owner.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{owner.Tenant.Slug}/members/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "https://api.aegisscribe.com/problems/last-owner",
            problem.GetProperty("type").GetString());

        Assert.Equal(TenantRole.Owner, await RoleOfAsync(owner.Tenant.Id, owner.UserId));
    }

    [Fact]
    public async Task AnOwnerMayWalkOutOnceSomebodyElseHoldsTheCommunity()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var owner = scenario.TenantA;
        var second = await TenantSide.JoinAsync(fixture, owner.Tenant, TenantRole.Owner);

        var response = await owner.SendAsync(HttpMethod.Delete, $"/api/v1/t/{owner.Tenant.Slug}/members/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await RoleOfAsync(owner.Tenant.Id, owner.UserId));
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(owner.Tenant.Id, second.UserId));
    }

    [Fact]
    public async Task AfterLeaving_TheCommunitysRoutesAre404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        var route = $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/roster";

        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync(HttpMethod.Get, route)).StatusCode);

        await member.SendAsync(HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");

        // A former member is a stranger: 404, never 403, because a 403 would confirm the community
        // exists (tenancy.md).
        Assert.Equal(HttpStatusCode.NotFound, (await member.SendAsync(HttpMethod.Get, route)).StatusCode);
    }

    [Fact]
    public async Task LeavingIsAuditedAsWalkingOutRatherThanAsBeingRemoved()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        await member.SendAsync(HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var rows = await verify.AuditLogs.ToListAsync();

        // "Did somebody push them out, or did they walk" is the question this table gets asked.
        var left = Assert.Single(rows, row => row.Action == AuditAction.MemberLeft);
        Assert.Equal(member.UserId, left.ActorUserId);
        Assert.Equal(member.UserId, left.SubjectUserId);
        Assert.Equal(nameof(TenantRole.Member), left.Before);
        Assert.Equal("Left", left.After);

        Assert.DoesNotContain(rows, row => row.Action == AuditAction.MemberRemoved);
    }

    // ---- invitations ----

    [Fact]
    public async Task CreatingAnInvitationReturnsTheTokenOnceAndStoresOnlyItsHash()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var created = await CreateInvitationAsync(scenario.TenantA, TenantRole.Member, note: "for Thalric");

        Assert.False(string.IsNullOrWhiteSpace(created.Token));
        Assert.Equal(InvitationStatus.Pending, created.Invitation.Status);
        Assert.Equal(TenantRole.Member, created.Invitation.Role);
        Assert.Equal("for Thalric", created.Invitation.Note);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var row = await verify.TenantInvitations.FirstAsync(i => i.Id == created.Invitation.Id);

        // A live token grants membership to whoever holds it, so it is stored the way a password is:
        // not at all. What is on the row is a hash, and it is the hash OF the token we were handed.
        Assert.Equal(InvitationTokens.Hash(created.Token), row.TokenHash);
        Assert.DoesNotContain(created.Token, System.Text.Encoding.UTF8.GetString(row.TokenHash));
    }

    [Fact]
    public async Task TheInvitationListNeverCarriesATokenBack()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var created = await CreateInvitationAsync(scenario.TenantA, TenantRole.Member);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations");
        var body = await response.Content.ReadAsStringAsync();

        // Officer-level read access must not become the ability to join as anybody.
        Assert.DoesNotContain(created.Token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnOfficerMayNotMintAnOwnerInvitation()
    {
        // An invitation is a role grant with a delay on it, so it is bounded by exactly what a direct
        // promotion is. Without this an officer mints an Owner token and hands it to themselves.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations",
            new CreateInvitationViewModel { Role = TenantRole.Owner });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        // Filtered by tenant BY HAND, and that is not incidental: TenantInvitations has no query
        // filter, so a bare ToListAsync() here reads every community's invitations and this assertion
        // would fail on whatever another test happened to mint. The production code carries the same
        // obligation, which is why every repository method takes a tenantId.
        Assert.Empty(
            await verify.TenantInvitations.Where(i => i.TenantId == scenario.TenantA.Tenant.Id).ToListAsync());
    }

    [Fact]
    public async Task RevokingMarksTheInvitationDeadAndIsIdempotent()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var created = await CreateInvitationAsync(scenario.TenantA, TenantRole.Member);
        var route = $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations/{created.Invitation.Id}";

        Assert.Equal(
            HttpStatusCode.NoContent, (await scenario.TenantA.SendAsync(HttpMethod.Delete, route)).StatusCode);

        // Twice is the same answer — the intent is "this link is dead" and it already is.
        Assert.Equal(
            HttpStatusCode.NoContent, (await scenario.TenantA.SendAsync(HttpMethod.Delete, route)).StatusCode);

        var listed = await ListInvitationsAsync(scenario.TenantA);
        Assert.Equal(InvitationStatus.Revoked, Assert.Single(listed).Status);
    }

    [Fact]
    public async Task AnInvitationPastItsExpiryReadsAsExpiredWithoutAnythingHavingSweptIt()
    {
        // Expiry is derived from the clock, never stored — a status column saying "Pending" an hour
        // after the row expired would need a sweeper to stay true, and would be wrong until it ran.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var created = await CreateInvitationAsync(scenario.TenantA, TenantRole.Member);

        await using (var age = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            var row = await age.TenantInvitations.FirstAsync(i => i.Id == created.Invitation.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await age.SaveChangesAsync();
        }

        var listed = await ListInvitationsAsync(scenario.TenantA);
        Assert.Equal(InvitationStatus.Expired, Assert.Single(listed).Status);
    }

    [Fact]
    public async Task AnInvitationLongerThanTheCeilingIsRefused()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations",
            new CreateInvitationViewModel { Role = TenantRole.Member, ExpiresInDays = 365 });

        // A link that outlives the reason it was made is a standing key to the community.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- join requests ----

    [Fact]
    public async Task AStrangerMayAskOnlyWhereTheDoorIsOpen()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();
        var route = $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests";

        // Closed by default — and "closed" is indistinguishable from "no such community", so this
        // route cannot be walked to enumerate every community on the platform.
        var shut = await stranger.SendAsync(HttpMethod.Post, route, new CreateJoinRequestViewModel());
        Assert.Equal(HttpStatusCode.NotFound, shut.StatusCode);

        var nonexistent = await stranger.SendAsync(
            HttpMethod.Post, "/api/v1/communities/no-such-community-at-all/join-requests",
            new CreateJoinRequestViewModel());
        Assert.Equal(HttpStatusCode.NotFound, nonexistent.StatusCode);

        await OpenTheDoorAsync(scenario.TenantA);

        var open = await stranger.SendAsync(
            HttpMethod.Post, route, new CreateJoinRequestViewModel { Message = "Resto druid, 3/8M." });
        Assert.Equal(HttpStatusCode.NoContent, open.StatusCode);
    }

    [Fact]
    public async Task ASecondPendingRequestFromTheSamePersonIsRefused()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();
        await OpenTheDoorAsync(scenario.TenantA);

        var route = $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests";

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await stranger.SendAsync(HttpMethod.Post, route, new CreateJoinRequestViewModel())).StatusCode);

        var second = await stranger.SendAsync(HttpMethod.Post, route, new CreateJoinRequestViewModel());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ApprovingCreatesTheMembershipAndCannotBeDoneTwice()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();
        var requestId = await RequestToJoinAsync(scenario.TenantA, stranger);

        var route = $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-requests/{requestId}/approve";
        var body = new SetMemberRoleViewModel { Role = TenantRole.Member };

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await scenario.TenantA.SendAsync(HttpMethod.Post, route, body)).StatusCode);

        Assert.Equal(TenantRole.Member, await RoleOfAsync(scenario.TenantA.Tenant.Id, stranger.UserId));

        // Decided is decided. Two officers working the same queue is ordinary, and the second click
        // must not produce a second membership.
        var again = await scenario.TenantA.SendAsync(HttpMethod.Post, route, body);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Equal(
            1,
            await verify.TenantMemberships
                .CountAsync(m => m.TenantId == scenario.TenantA.Tenant.Id && m.UserId == stranger.UserId));
    }

    [Fact]
    public async Task AnOfficerMayNotApproveSomebodyStraightInAsOwner()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var officer = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        var stranger = await StrangerAsync();
        var requestId = await RequestToJoinAsync(scenario.TenantA, stranger);

        // Approving is a role grant however it is spelled, so it carries the same ceiling as an
        // invitation and a direct promotion.
        var response = await officer.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-requests/{requestId}/approve",
            new SetMemberRoleViewModel { Role = TenantRole.Owner });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, stranger.UserId));
    }

    [Fact]
    public async Task DecliningCreatesNoMembershipAndLetsThePersonAskAgain()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();
        var requestId = await RequestToJoinAsync(scenario.TenantA, stranger);

        var declined = await scenario.TenantA.SendAsync(
            HttpMethod.Post, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-requests/{requestId}/decline");
        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);

        Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, stranger.UserId));

        // The unique index is FILTERED on Pending, which is what keeps the declined row as history
        // while still letting the same person ask again later.
        var again = await stranger.SendAsync(
            HttpMethod.Post,
            $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests",
            new CreateJoinRequestViewModel());
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        // Tenant named explicitly — TenantJoinRequests has no query filter either.
        Assert.Equal(
            2,
            await verify.TenantJoinRequests.CountAsync(
                r => r.TenantId == scenario.TenantA.Tenant.Id && r.UserId == stranger.UserId));
    }

    [Fact]
    public async Task AMemberMayNotSeeOrDecideTheQueue()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-requests");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- audit ----

    [Fact]
    public async Task EveryLifecycleActWritesExactlyOneAuditRowWithTheBeforeAndAfter()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var tenantId = scenario.TenantA.Tenant.Id;
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        await SetRoleAsync(scenario.TenantA, member.UserId, TenantRole.Officer);
        var invitation = await CreateInvitationAsync(scenario.TenantA, TenantRole.Member);
        await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations/{invitation.Invitation.Id}");
        await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{member.UserId}");

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var rows = await verify.AuditLogs.OrderBy(row => row.OccurredAt).ToListAsync();

        var roleChange = Assert.Single(rows, row => row.Action == AuditAction.MemberRoleChanged);
        Assert.Equal(scenario.TenantA.UserId, roleChange.ActorUserId);
        Assert.Equal(member.UserId, roleChange.SubjectUserId);
        Assert.Equal(nameof(TenantRole.Member), roleChange.Before);
        Assert.Equal(nameof(TenantRole.Officer), roleChange.After);

        Assert.Single(rows, row => row.Action == AuditAction.MemberInvited);
        Assert.Single(rows, row => row.Action == AuditAction.InvitationRevoked);

        var removal = Assert.Single(rows, row => row.Action == AuditAction.MemberRemoved);
        Assert.Equal(member.UserId, removal.SubjectUserId);
        Assert.Equal("Removed", removal.After);
    }

    [Fact]
    public async Task ARefusedActWritesNoAuditRow()
    {
        // A row recording a demotion the last-owner rule refused would be worse than no row at all.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var refused = await SetRoleAsync(scenario.TenantA, scenario.TenantA.UserId, TenantRole.Member);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    // ---- the member list ----

    [Fact]
    public async Task MembersCanSeeWhoIsInTheirCommunityButNotEachOthersEmail()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var member = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<CursorPageServiceModel<TenantMemberServiceModel>>(body, Json)!;

        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, m => m.UserId == member.UserId && m.Role == TenantRole.Member);

        // DisplayName only — a fellow member's contact details are not theirs to see.
        Assert.DoesNotContain(member.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scenario.TenantA.Email, body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----

    private Task<HttpResponseMessage> SetRoleAsync(TenantSide actor, string targetUserId, TenantRole role) =>
        actor.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{actor.Tenant.Slug}/members/{targetUserId}/role",
            new SetMemberRoleViewModel { Role = role });

    private async Task<TenantRole?> RoleOfAsync(Guid tenantId, string userId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        return await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => (TenantRole?)m.Role)
            .FirstOrDefaultAsync();
    }

    private async Task<CreatedInvitationServiceModel> CreateInvitationAsync(
        TenantSide actor, TenantRole role, string? note = null)
    {
        var response = await actor.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{actor.Tenant.Slug}/invitations",
            new CreateInvitationViewModel { Role = role, Note = note });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedInvitationServiceModel>(Json))!;
    }

    private async Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(TenantSide actor)
    {
        var response = await actor.SendAsync(HttpMethod.Get, $"/api/v1/t/{actor.Tenant.Slug}/invitations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<InvitationServiceModel>>(Json))!;
    }

    // A signed-in user with no membership anywhere — the only kind of person who can use the
    // tenant-less join-request route.
    private async Task<TenantSide> StrangerAsync()
    {
        var throwaway = await TenantSeeding.CreateTenantAsync(fixture);

        return await TenantSide.JoinAsync(fixture, throwaway, TenantRole.Owner);
    }

    private async Task OpenTheDoorAsync(TenantSide owner)
    {
        var response = await owner.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{owner.Tenant.Slug}/join-policy",
            new SetJoinPolicyViewModel { AcceptsJoinRequests = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Guid> RequestToJoinAsync(TenantSide owner, TenantSide stranger)
    {
        await OpenTheDoorAsync(owner);

        var response = await stranger.SendAsync(
            HttpMethod.Post,
            $"/api/v1/communities/{owner.Tenant.Slug}/join-requests",
            new CreateJoinRequestViewModel());
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, owner.Tenant.Id);

        return (await db.TenantJoinRequests.FirstAsync(
            r => r.TenantId == owner.Tenant.Id && r.UserId == stranger.UserId)).Id;
    }

    private async Task<Domain.Managers.Models.Domain.Character> SeedCharacterAsync()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        return await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
    }

    private async Task<Guid> SeedRosterEntryAsync(Guid tenantId, Guid characterId, Guid? mainRosterEntryId = null)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = characterId,
            MainRosterEntryId = mainRosterEntryId,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        db.RosterEntries.Add(entry);
        await db.SaveChangesAsync();

        return entry.Id;
    }

    private async Task SeedClaimAsync(Guid tenantId, Guid characterId, string userId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        db.CharacterClaims.Add(new CharacterClaim
        {
            Id = Guid.NewGuid(),
            CharacterId = characterId,
            UserId = userId,
            ClaimedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }
}
