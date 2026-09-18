using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 8.3's two-tenant test, and it carries more weight than most.
//
// TenantInvitation and TenantJoinRequest deliberately do NOT implement ITenantScoped — the person on
// the other side of both is not a member yet, so there is no resolved tenant for a query filter to
// read or for the interceptor to stamp from. That is the same documented bargain TenantMembership
// makes, and its price is that the "forgot the filter" safety net does not exist here: isolation rests
// on every repository method taking a tenantId, and on that id always being ITenantContext.TenantId.
//
// These tests are what holds that up. If a tenantId parameter is ever dropped for convenience, this is
// the file that goes red.
[Collection("AegisScribe API")]
public class MembershipIsolationTests(AegisScribeAppFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task NeitherCommunitySeesTheOthersMembers()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var inA = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        var inB = await TenantSide.JoinAsync(fixture, scenario.TenantB.Tenant, TenantRole.Member);

        var aMembers = await ListMembersAsync(scenario.TenantA);
        var bMembers = await ListMembersAsync(scenario.TenantB);

        Assert.Contains(aMembers, m => m.UserId == inA.UserId);
        Assert.DoesNotContain(aMembers, m => m.UserId == inB.UserId);
        Assert.DoesNotContain(aMembers, m => m.UserId == scenario.TenantB.UserId);

        Assert.Contains(bMembers, m => m.UserId == inB.UserId);
        Assert.DoesNotContain(bMembers, m => m.UserId == inA.UserId);
    }

    [Fact]
    public async Task AClaimMadeInOneCommunityDoesNotFollowTheSamePersonIntoAnother()
    {
        // 8.4 hangs each member's claimed characters off the member projection, which means the member
        // list now reads a SECOND tenant-scoped table. A claim is one community's business — the same
        // player may be a known raider in A and an unvouched stranger in B — so the join owes this test.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var person = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        await TenantSeeding.AddMembershipAsync(
            fixture, scenario.TenantB.Tenant.Id, person.UserId, TenantRole.Member);

        // ONE global character row, claimed in A only. That is the whole shape of the two zones.
        var realm = await Character.CharacterSeeding.CreateRealmAsync(fixture, region: "eu");
        var character = await Character.CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
        await SeedClaimAsync(scenario.TenantA.Tenant.Id, character.Id, person.UserId);

        var inA = Assert.Single(await ListMembersAsync(scenario.TenantA), m => m.UserId == person.UserId);
        var claimed = Assert.Single(inA.ClaimedCharacters);
        Assert.Equal(character.Id, claimed.CharacterId);
        Assert.Equal(character.Name, claimed.Name);
        Assert.Equal(realm.Slug, claimed.RealmSlug);
        Assert.Equal("eu", claimed.Region);

        // B sees the same person with nothing claimed — and an EMPTY list rather than null, because the
        // officer screen renders "no character claimed" as a line of its own.
        var inB = Assert.Single(await ListMembersAsync(scenario.TenantB), m => m.UserId == person.UserId);
        Assert.NotNull(inB.ClaimedCharacters);
        Assert.Empty(inB.ClaimedCharacters);
    }

    [Fact]
    public async Task AMembersClaimsAllListTogetherOnTheirRow()
    {
        // The multi-claim case, which is why the claims are a second query rather than a join into the
        // page: joined, two claims would have become two member rows and eaten the page limit.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var person = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);

        var realm = await Character.CharacterSeeding.CreateRealmAsync(fixture);
        var first = await Character.CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: "Aardwolf");
        var second = await Character.CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: "Zephyrine");

        await SeedClaimAsync(scenario.TenantA.Tenant.Id, second.Id, person.UserId);
        await SeedClaimAsync(scenario.TenantA.Tenant.Id, first.Id, person.UserId);

        var member = Assert.Single(await ListMembersAsync(scenario.TenantA), m => m.UserId == person.UserId);

        // Ordered by name, so two officers looking at the same member see the same list.
        Assert.Equal(["Aardwolf", "Zephyrine"], member.ClaimedCharacters.Select(c => c.Name));

        // The class colour is mapped API-side; the frontend is forbidden a class→hex table of its own.
        Assert.All(member.ClaimedCharacters, c => Assert.StartsWith("#", c.ClassColor));

        // The owner who created the community has claimed nothing, and says so with an empty list.
        var owner = Assert.Single(
            await ListMembersAsync(scenario.TenantA), m => m.UserId == scenario.TenantA.UserId);
        Assert.Empty(owner.ClaimedCharacters);
    }

    [Fact]
    public async Task ClaimsFollowTheirMemberOntoEveryPage_NotJustTheFirst()
    {
        // The claims are fetched per page, keyed on the user ids that page happens to hold. A member
        // who lands on page two must arrive with their claims like anybody else — the failure mode
        // this guards is a screen that quietly stops showing claims once the list is long enough to
        // scroll, which nothing else here would catch.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var realm = await Character.CharacterSeeding.CreateRealmAsync(fixture);

        var claimants = new List<(string UserId, Guid CharacterId)>();

        for (var i = 0; i < 3; i++)
        {
            var person = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
            var character = await Character.CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
            await SeedClaimAsync(scenario.TenantA.Tenant.Id, character.Id, person.UserId);
            claimants.Add((person.UserId, character.Id));
        }

        // One member per page, so every claimant after the first is only reachable through a cursor.
        var seen = new Dictionary<string, IReadOnlyList<MemberClaimedCharacterServiceModel>>();
        string? cursor = null;

        do
        {
            var page = await MemberPageAsync(scenario.TenantA, limit: 1, cursor: cursor);

            foreach (var member in page.Items)
            {
                seen[member.UserId] = member.ClaimedCharacters;
            }

            cursor = page.HasMore ? page.NextCursor : null;
        }
        while (cursor is not null);

        foreach (var (userId, characterId) in claimants)
        {
            var claimed = Assert.Single(seen[userId]);
            Assert.Equal(characterId, claimed.CharacterId);
        }

        // And the owner, who claimed nothing, still came back with an empty list rather than a null.
        Assert.Empty(seen[scenario.TenantA.UserId]);
    }

    [Fact]
    public async Task AnInvitationMintedInOneCommunityIsInvisibleInTheOther()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var created = await CreateInvitationAsync(scenario.TenantA);

        // B's list is B's. A missing tenantId on the invitation list would show every community's
        // outstanding links to everybody — and the rows have no query filter to catch it.
        Assert.Empty(await ListInvitationsAsync(scenario.TenantB));
        Assert.Single(await ListInvitationsAsync(scenario.TenantA));

        // B cannot revoke it either. The revoke is scoped by tenant in the statement's own WHERE, so
        // this is a no-op rather than a refusal — and A's invitation survives.
        var revoke = await scenario.TenantB.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/invitations/{created.Invitation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(InvitationStatus.Pending, Assert.Single(await ListInvitationsAsync(scenario.TenantA)).Status);
    }

    [Fact]
    public async Task AJoinRequestToOneCommunityNeverAppearsInAnothersQueue()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();

        await OpenTheDoorAsync(scenario.TenantA);
        await OpenTheDoorAsync(scenario.TenantB);

        var response = await stranger.SendAsync(
            HttpMethod.Post,
            $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests",
            new CreateJoinRequestViewModel { Message = "Only for A." });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Single(await ListJoinRequestsAsync(scenario.TenantA));
        Assert.Empty(await ListJoinRequestsAsync(scenario.TenantB));
    }

    [Fact]
    public async Task OneCommunityCannotApproveOrDeclineAnothersRequest()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var stranger = await StrangerAsync();
        await OpenTheDoorAsync(scenario.TenantA);

        await stranger.SendAsync(
            HttpMethod.Post,
            $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests",
            new CreateJoinRequestViewModel());

        Guid requestId;

        await using (var db = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            // Tenant named explicitly: TenantJoinRequests has no query filter, which is the whole
            // reason this file exists.
            requestId = (await db.TenantJoinRequests.FirstAsync(
                r => r.TenantId == scenario.TenantA.Tenant.Id && r.UserId == stranger.UserId)).Id;
        }

        // B holds the id — guessing a GUID is not the leak. What B must not be able to do is turn A's
        // applicant into a member of B, which is exactly what a dropped tenantId would allow.
        var approve = await scenario.TenantB.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/join-requests/{requestId}/approve",
            new SetMemberRoleViewModel { Role = TenantRole.Member });
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);

        var decline = await scenario.TenantB.SendAsync(
            HttpMethod.Post, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/join-requests/{requestId}/decline");
        Assert.Equal(HttpStatusCode.NotFound, decline.StatusCode);

        // Nobody joined anything, and A's request is still pending for A to decide.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);
        Assert.False(await verify.TenantMemberships
            .AnyAsync(m => m.TenantId == scenario.TenantB.Tenant.Id && m.UserId == stranger.UserId));

        Assert.Equal(JoinRequestStatus.Pending, Assert.Single(await ListJoinRequestsAsync(scenario.TenantA)).Status);
    }

    [Fact]
    public async Task OneCommunitysOwnersDoNotSatisfyAnothersLastOwnerRule()
    {
        // The subtlest one. The last-owner guard counts owners "in this tenant"; drop that clause and
        // the guard passes on the strength of some OTHER community's owners, and a community is left
        // with none.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        // B has its own owner, and A has exactly one. Across the platform there are two.
        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{scenario.TenantA.UserId}/role",
            new SetMemberRoleViewModel { Role = TenantRole.Member });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(scenario.TenantA.Tenant.Id, scenario.TenantA.UserId));
    }

    [Fact]
    public async Task RemovingSomebodyFromOneCommunityLeavesTheirStandingInAnotherIntact()
    {
        // The same person in two communities is the normal case, and the two memberships are unrelated
        // facts. Removing one must not touch the other — nor the roster entry it owns there.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var person = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Member);
        await TenantSeeding.AddMembershipAsync(
            fixture, scenario.TenantB.Tenant.Id, person.UserId, TenantRole.Officer);

        var character = await SeedCharacterAsync();
        await SeedRosterEntryAsync(scenario.TenantA.Tenant.Id, character.Id);
        await SeedRosterEntryAsync(scenario.TenantB.Tenant.Id, character.Id);
        await SeedClaimAsync(scenario.TenantA.Tenant.Id, character.Id, person.UserId);
        await SeedClaimAsync(scenario.TenantB.Tenant.Id, character.Id, person.UserId);

        var removed = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/{person.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Gone from A...
        await using (var aVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            Assert.False(await aVerify.TenantMemberships
                .AnyAsync(m => m.TenantId == scenario.TenantA.Tenant.Id && m.UserId == person.UserId));
            Assert.Empty(await aVerify.CharacterClaims.Where(c => c.UserId == person.UserId).ToListAsync());
            Assert.Empty(await aVerify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());
        }

        // ...and entirely untouched in B. The claim and roster deletes run under their entities' query
        // filters, which is what confines them to the community doing the removing.
        await using var bVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantB.Tenant.Id, person.UserId));
        Assert.Single(await bVerify.CharacterClaims.Where(c => c.UserId == person.UserId).ToListAsync());
        Assert.Single(await bVerify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());
    }

    [Fact]
    public async Task LeavingOneCommunityLeavesTheSamePersonsStandingInAnotherIntact()
    {
        // Leaving runs the same cascade a removal does, so it inherits the same isolation question:
        // the claim and roster deletes are confined by their entities' query filters to the community
        // being walked out of.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var person = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        await TenantSeeding.AddMembershipAsync(
            fixture, scenario.TenantB.Tenant.Id, person.UserId, TenantRole.Officer);

        var character = await SeedCharacterAsync();
        await SeedRosterEntryAsync(scenario.TenantA.Tenant.Id, character.Id);
        await SeedRosterEntryAsync(scenario.TenantB.Tenant.Id, character.Id);
        await SeedClaimAsync(scenario.TenantA.Tenant.Id, character.Id, person.UserId);
        await SeedClaimAsync(scenario.TenantB.Tenant.Id, character.Id, person.UserId);

        var left = await person.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);

        await using (var aVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, person.UserId));
            Assert.Empty(await aVerify.CharacterClaims.Where(c => c.UserId == person.UserId).ToListAsync());
            Assert.Empty(await aVerify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());
        }

        await using var bVerify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantB.Tenant.Id, person.UserId));
        Assert.Single(await bVerify.CharacterClaims.Where(c => c.UserId == person.UserId).ToListAsync());
        Assert.Single(await bVerify.RosterEntries.Where(e => e.CharacterId == character.Id).ToListAsync());
    }

    [Fact]
    public async Task OneCommunitysOwnersDoNotLetAnothersLastOwnerWalkOut()
    {
        // The leave path reaches the same guard as demote and remove, so it inherits the same
        // cross-tenant question: B having an owner must not satisfy A's invariant.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var response = await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/members/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TenantRole.Owner, await RoleOfAsync(scenario.TenantA.Tenant.Id, scenario.TenantA.UserId));
    }

    [Fact]
    public async Task ReachingAnotherCommunitysMembershipRoutesIs404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var slug = scenario.TenantA.Tenant.Slug;

        // 404 every time, never 403 — a 403 would confirm the community exists (tenancy.md).
        foreach (var (method, path) in new (HttpMethod, string)[]
        {
            (HttpMethod.Get, $"/api/v1/t/{slug}/members"),
            (HttpMethod.Get, $"/api/v1/t/{slug}/invitations"),
            (HttpMethod.Get, $"/api/v1/t/{slug}/join-requests"),
        })
        {
            var response = await scenario.TenantB.SendAsync(method, path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ---- helpers ----

    private async Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(TenantSide side) =>
        (await MemberPageAsync(side)).Items;

    private async Task<CursorPageServiceModel<TenantMemberServiceModel>> MemberPageAsync(
        TenantSide side, int? limit = null, string? cursor = null)
    {
        var query = new List<string>();

        if (limit is not null)
        {
            query.Add($"limit={limit}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var suffix = query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}";
        var response = await side.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/members{suffix}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<TenantMemberServiceModel>>(Json))!;
    }

    private async Task<IReadOnlyList<InvitationServiceModel>> ListInvitationsAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/invitations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<InvitationServiceModel>>(Json))!;
    }

    private async Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/join-requests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<JoinRequestServiceModel>>(Json))!;
    }

    private async Task<CreatedInvitationServiceModel> CreateInvitationAsync(TenantSide side)
    {
        var response = await side.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{side.Tenant.Slug}/invitations",
            new CreateInvitationViewModel { Role = TenantRole.Member });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedInvitationServiceModel>(Json))!;
    }

    private async Task OpenTheDoorAsync(TenantSide owner)
    {
        var response = await owner.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{owner.Tenant.Slug}/join-policy",
            new SetJoinPolicyViewModel { AcceptsJoinRequests = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<TenantSide> StrangerAsync()
    {
        var throwaway = await TenantSeeding.CreateTenantAsync(fixture);

        return await TenantSide.JoinAsync(fixture, throwaway, TenantRole.Owner);
    }

    private async Task<TenantRole?> RoleOfAsync(Guid tenantId, string userId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        return await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => (TenantRole?)m.Role)
            .FirstOrDefaultAsync();
    }

    private async Task<Domain.Managers.Models.Domain.Character> SeedCharacterAsync()
    {
        var realm = await Character.CharacterSeeding.CreateRealmAsync(fixture);

        return await Character.CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
    }

    private async Task SeedRosterEntryAsync(Guid tenantId, Guid characterId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        db.RosterEntries.Add(new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = characterId,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
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
