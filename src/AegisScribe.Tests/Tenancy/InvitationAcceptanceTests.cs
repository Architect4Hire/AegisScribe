using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 8.3b — spending an invitation.
//
// Two properties carry this whole feature, and most of the tests below exist for one of them:
//
//   SINGLE USE. One link admits at most one person, and that is decided inside the consuming UPDATE's
//   own WHERE rather than by a check in front of it. The membership primary key is the second lock
//   behind it.
//
//   A DEAD TOKEN NAMES NOTHING. Expired, consumed, revoked and unknown are told apart for the holder,
//   because what they should do next differs — but not one of the four reveals the community.
[Collection("AegisScribe API")]
public class InvitationAcceptanceTests(AegisScribeAppFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task AcceptingALiveTokenMakesTheCallerAMemberAtTheTokensRole()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var token = await MintAsync(scenario.TenantA, TenantRole.Officer);

        var accepted = await AcceptAsync(invited, token);

        Assert.Equal(scenario.TenantA.Tenant.Slug, accepted.TenantSlug);
        Assert.Equal(TenantRole.Officer, accepted.Role);
        Assert.False(accepted.AlreadyAMember);

        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantA.Tenant.Id, invited.UserId));
    }

    [Fact]
    public async Task AcceptingCreatesNoRosterEntryAndNoCharacterClaim()
    {
        // 8.2's rule, restated: joining a community says nothing about which characters you play.
        // Claiming is the member's own separate act.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();

        await AcceptAsync(invited, await MintAsync(scenario.TenantA, TenantRole.Member));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Empty(await verify.RosterEntries.ToListAsync());
        Assert.Empty(await verify.CharacterClaims.ToListAsync());
    }

    [Fact]
    public async Task ATokenCanOnlyBeSpentOnce()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var first = await StrangerAsync();
        var second = await StrangerAsync();
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        await AcceptAsync(first, token);

        // The forwarded link. Whoever got there second is refused, and refused as CONSUMED rather
        // than with a generic failure.
        var response = await Post(second, token);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("consumed", await RefusalTypeAsync(response));
        Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, second.UserId));
    }

    [Fact]
    public async Task TwoPeopleAcceptingAtOnceProduceExactlyOneMembership()
    {
        // The test the consuming UPDATE exists for. A read-then-write both sees live, both writes, and
        // one invitation admits two people — nothing conflicts and nothing throws.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var first = await StrangerAsync();
        var second = await StrangerAsync();
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        var responses = await Task.WhenAll(Post(first, token), Post(second, token));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Gone));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var joined = await verify.TenantMemberships
            .CountAsync(m => m.TenantId == scenario.TenantA.Tenant.Id
                && (m.UserId == first.UserId || m.UserId == second.UserId));

        Assert.Equal(1, joined);

        // And the invitation records exactly one accepter.
        var invitation = await verify.TenantInvitations
            .SingleAsync(i => i.TenantId == scenario.TenantA.Tenant.Id);
        Assert.NotNull(invitation.AcceptedAt);
        Assert.NotNull(invitation.AcceptedByUserId);
    }

    [Fact]
    public async Task ARevokedTokenIsRefusedAsRevoked()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var (token, invitationId) = await MintWithIdAsync(scenario.TenantA, TenantRole.Member);

        await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/invitations/{invitationId}");

        var response = await Post(invited, token);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("revoked", await RefusalTypeAsync(response));
        Assert.Null(await RoleOfAsync(scenario.TenantA.Tenant.Id, invited.UserId));
    }

    [Fact]
    public async Task AnExpiredTokenIsRefusedAsExpired()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var (token, invitationId) = await MintWithIdAsync(scenario.TenantA, TenantRole.Member);

        await ExpireAsync(scenario.TenantA.Tenant.Id, invitationId);

        var response = await Post(invited, token);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("expired", await RefusalTypeAsync(response));
    }

    [Fact]
    public async Task AnUnknownTokenIs404AndSaysNothingAtAll()
    {
        var invited = await StrangerAsync();

        var response = await Post(invited, "not-a-real-token-at-all");

        // 404, not 410: a token that never existed stays indistinguishable from nothing, while the
        // three dead states above are distinguishable on purpose.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoRefusalEverNamesTheCommunity()
    {
        // The property that makes a dead token worthless rather than merely unusable. Every refusal
        // body is checked against both the community's name and its slug.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var tenant = scenario.TenantA.Tenant;

        var revoked = await MintWithIdAsync(scenario.TenantA, TenantRole.Member);
        await scenario.TenantA.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{tenant.Slug}/invitations/{revoked.InvitationId}");

        var expired = await MintWithIdAsync(scenario.TenantA, TenantRole.Member);
        await ExpireAsync(tenant.Id, expired.InvitationId);

        var spent = await MintAsync(scenario.TenantA, TenantRole.Member);
        await AcceptAsync(await StrangerAsync(), spent);

        foreach (var token in new[] { revoked.Token, expired.Token, spent, "no-such-token" })
        {
            foreach (var body in new[]
            {
                await (await Post(invited, token)).Content.ReadAsStringAsync(),
                await (await Preview(token)).Content.ReadAsStringAsync(),
            })
            {
                Assert.DoesNotContain(tenant.Name, body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(tenant.Slug, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---- the preview ----

    [Fact]
    public async Task ALiveTokenPreviewsAnonymouslyAndNamesTheCommunityAndRole()
    {
        // Anonymous on purpose: somebody arriving on a link should see what they were invited to
        // BEFORE deciding to sign in, rather than signing in to find out.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var token = await MintAsync(scenario.TenantA, TenantRole.Officer);

        var response = await Preview(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = (await response.Content.ReadFromJsonAsync<InvitationPreviewServiceModel>(Json))!;

        Assert.Equal(scenario.TenantA.Tenant.Slug, preview.TenantSlug);
        Assert.Equal(scenario.TenantA.Tenant.Name, preview.TenantName);
        Assert.Equal(TenantRole.Officer, preview.Role);
    }

    [Fact]
    public async Task PreviewingDoesNotSpendTheToken()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        await Preview(token);
        await Preview(token);

        // Still live, and still worth exactly one membership.
        var accepted = await AcceptAsync(invited, token);
        Assert.False(accepted.AlreadyAMember);
    }

    // ---- already a member ----

    [Fact]
    public async Task AcceptingWhenAlreadyInIsANoOpThatDoesNotSpendTheToken()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var existing = await TenantSide.JoinAsync(fixture, scenario.TenantA.Tenant, TenantRole.Officer);
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        var accepted = await AcceptAsync(existing, token);

        // Lands them in the community, reports the truth, and changes NOTHING — an Officer clicking a
        // Member link stays an Officer. No silent role change in either direction.
        Assert.True(accepted.AlreadyAMember);
        Assert.Equal(TenantRole.Officer, accepted.Role);
        Assert.Equal(scenario.TenantA.Tenant.Slug, accepted.TenantSlug);
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantA.Tenant.Id, existing.UserId));

        await using (var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id))
        {
            // No second row — the primary key makes that unrepresentable, and nothing tried.
            Assert.Equal(
                1,
                await verify.TenantMemberships.CountAsync(
                    m => m.TenantId == scenario.TenantA.Tenant.Id && m.UserId == existing.UserId));
        }

        // And the token was NOT spent, so it is still worth something to whoever it was meant for.
        var invited = await StrangerAsync();
        Assert.False((await AcceptAsync(invited, token)).AlreadyAMember);
    }

    [Fact]
    public async Task AcceptingResolvesTheAccepterS_PendingJoinRequest()
    {
        // Two doors into one community can be open at once. Without this the request outlives the
        // joining, and the next officer to click approve hits the membership primary key.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();

        await scenario.TenantA.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-policy",
            new SetJoinPolicyViewModel { AcceptsJoinRequests = true });

        await invited.SendAsync(
            HttpMethod.Post,
            $"/api/v1/communities/{scenario.TenantA.Tenant.Slug}/join-requests",
            new CreateJoinRequestViewModel());

        await AcceptAsync(invited, await MintAsync(scenario.TenantA, TenantRole.Member));

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var request = await verify.TenantJoinRequests.SingleAsync(
            r => r.TenantId == scenario.TenantA.Tenant.Id && r.UserId == invited.UserId);

        Assert.Equal(JoinRequestStatus.Approved, request.Status);
        Assert.NotNull(request.DecidedAt);

        // And the officer's queue is clear, so nobody can click approve on a member.
        var queue = await scenario.TenantA.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/join-requests?status=Pending");
        Assert.Empty((await queue.Content.ReadFromJsonAsync<List<JoinRequestServiceModel>>(Json))!);
    }

    // ---- audit ----

    [Fact]
    public async Task AcceptingAndRefusingBothLandInTheCommunitysAuditLog()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        await AcceptAsync(invited, token);

        // Somebody tries the spent link. "Someone used this after I sent it on" is a question an
        // officer will ask.
        await Post(await StrangerAsync(), token);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var rows = await verify.AuditLogs.ToListAsync();

        var accepted = Assert.Single(rows, row => row.Action == AuditAction.InvitationAccepted);
        Assert.Equal(invited.UserId, accepted.ActorUserId);
        Assert.Equal(invited.UserId, accepted.SubjectUserId);

        var refused = Assert.Single(rows, row => row.Action == AuditAction.InvitationRefused);
        Assert.Equal(nameof(InvitationRefusal.Consumed), refused.After);
    }

    // ---- two tenants ----

    [Fact]
    public async Task ATokenForOneCommunityGrantsNothingWhatsoeverInAnother()
    {
        // The two-tenant test 8.3b asks for. The token is the ONLY thing naming a community, so the
        // failure it guards against is a token for A somehow yielding standing in B.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var invited = await StrangerAsync();

        var token = await MintAsync(scenario.TenantA, TenantRole.Officer);
        await AcceptAsync(invited, token);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id);

        // In A, and only A.
        Assert.Equal(TenantRole.Officer, await RoleOfAsync(scenario.TenantA.Tenant.Id, invited.UserId));
        Assert.Null(await RoleOfAsync(scenario.TenantB.Tenant.Id, invited.UserId));

        // B's routes are 404 to them, never 403.
        var response = await invited.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/roster");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And B's officers never saw the invitation or its audit rows.
        Assert.Empty(await verify.TenantInvitations
            .Where(i => i.TenantId == scenario.TenantB.Tenant.Id).ToListAsync());
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task TheStoredRowNeverHoldsTheTokenItself()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var token = await MintAsync(scenario.TenantA, TenantRole.Member);

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        var row = await verify.TenantInvitations.SingleAsync(i => i.TenantId == scenario.TenantA.Tenant.Id);

        // A leaked backup must not be replayable as a pile of live invitations.
        Assert.Equal(Domain.Managers.InvitationTokens.Hash(token), row.TokenHash);
        Assert.DoesNotContain(token, System.Text.Encoding.UTF8.GetString(row.TokenHash), StringComparison.Ordinal);
    }

    // ---- helpers ----

    private async Task<string> MintAsync(TenantSide officer, TenantRole role) =>
        (await MintWithIdAsync(officer, role)).Token;

    private async Task<(string Token, Guid InvitationId)> MintWithIdAsync(TenantSide officer, TenantRole role)
    {
        var response = await officer.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{officer.Tenant.Slug}/invitations",
            new CreateInvitationViewModel { Role = role });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<CreatedInvitationServiceModel>(Json))!;

        return (created.Token, created.Invitation.Id);
    }

    private async Task ExpireAsync(Guid tenantId, Guid invitationId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var row = await db.TenantInvitations.FirstAsync(i => i.Id == invitationId);
        row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private async Task<InvitationAcceptedServiceModel> AcceptAsync(TenantSide caller, string token)
    {
        var response = await Post(caller, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<InvitationAcceptedServiceModel>(Json))!;
    }

    // The token rides in the BODY, never the URL: ASP.NET Core's RequestPath scope would otherwise
    // stamp it onto every log entry the request writes. See InvitationTokenSecrecyTests.
    private static Task<HttpResponseMessage> Post(TenantSide caller, string token) =>
        caller.SendAsync(HttpMethod.Post, "/api/v1/invitations/accept", new InvitationTokenViewModel { Token = token });

    // Anonymous, so it deliberately does not go through TenantSide — there is no bearer token to send,
    // and sending one would not test the thing this endpoint promises.
    private async Task<HttpResponseMessage> Preview(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/invitations/preview")
        {
            Content = JsonContent.Create(new InvitationTokenViewModel { Token = token }),
        };

        return await fixture.ApiClient.SendAsync(request);
    }

    // The trailing segment of the problem `type` — what a client actually branches on
    // (api-contract.md), rather than the prose.
    private static async Task<string> RefusalTypeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.GetProperty("type").GetString()!.Split('/')[^1].Replace("invitation-", string.Empty);
    }

    private async Task<TenantRole?> RoleOfAsync(Guid tenantId, string userId)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        return await db.TenantMemberships
            .Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => (TenantRole?)m.Role)
            .FirstOrDefaultAsync();
    }

    // A signed-in user with no membership in either community under test.
    private async Task<TenantSide> StrangerAsync()
    {
        var throwaway = await TenantSeeding.CreateTenantAsync(fixture);

        return await TenantSide.JoinAsync(fixture, throwaway, TenantRole.Owner);
    }
}
