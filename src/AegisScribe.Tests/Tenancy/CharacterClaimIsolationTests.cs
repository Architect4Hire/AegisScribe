using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 7.2b's two-tenant test, in the shape the prompt names: claiming in tenant A must leave the same
// Character's standing in tenant B untouched.
//
// Every case shares ONE global Character between both communities, because that is the arrangement a
// broken filter cannot survive — and because it is the honest one. The same player really can be in
// two communities, and each community's view of who owns that character is its own.
[Collection("AegisScribe API")]
public class CharacterClaimIsolationTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task ClaimingInOneCommunity_LeavesTheSameCharacterUnclaimedInAnother()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();

        var claimed = await ClaimAsync(scenario.TenantA, shared.Id);
        Assert.Equal(scenario.TenantA.UserId, claimed.ClaimedByUserId);

        // B sees the very same character as unclaimed — 200 with nulls, not a 404, and emphatically
        // not A's holder.
        var inB = await GetAsync(scenario.TenantB, shared.Id);
        Assert.Null(inB.ClaimedByUserId);
        Assert.Null(inB.ClaimedByDisplayName);
        Assert.Null(inB.ClaimedAt);

        // And B's own member may claim it there. Two live claims on one global Character, one per
        // community, both correct — the whole reason CharacterClaim is tenant-scoped.
        var claimedInB = await ClaimAsync(scenario.TenantB, shared.Id);
        Assert.Equal(scenario.TenantB.UserId, claimedInB.ClaimedByUserId);

        // A's claim is undisturbed by B's.
        Assert.Equal(scenario.TenantA.UserId, (await GetAsync(scenario.TenantA, shared.Id)).ClaimedByUserId);
    }

    [Fact]
    public async Task AnOfficersClearInOneCommunity_DoesNotReachTheOthersClaim()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();

        await ClaimAsync(scenario.TenantA, shared.Id);
        await ClaimAsync(scenario.TenantB, shared.Id);

        // B's officer clears the claim on this character — in B.
        var cleared = await scenario.TenantB.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/claims/{shared.Id}/holder");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        Assert.Null((await GetAsync(scenario.TenantB, shared.Id)).ClaimedByUserId);

        // A's claim on the same character survives, which is the assertion that fails if the clear
        // reached past the query filter.
        Assert.Equal(scenario.TenantA.UserId, (await GetAsync(scenario.TenantA, shared.Id)).ClaimedByUserId);

        // Confirmed at rest, not only through A's (cached) read.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.True(await verify.CharacterClaims.AnyAsync(c => c.CharacterId == shared.Id));
    }

    [Fact]
    public async Task AnAuditRowIsWrittenInTheActingCommunityOnly()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();

        await ClaimAsync(scenario.TenantA, shared.Id);
        await ClaimAsync(scenario.TenantB, shared.Id);

        await scenario.TenantB.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/claims/{shared.Id}/holder");

        await using (var inB = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantB.Tenant.Id))
        {
            var entry = Assert.Single(await inB.AuditLogs.Where(a => a.TargetId == shared.Id).ToListAsync());
            Assert.Equal(AuditAction.CharacterClaimCleared, entry.Action);
            Assert.Equal(scenario.TenantB.UserId, entry.ActorUserId);
            Assert.Equal(scenario.TenantB.UserId, entry.SubjectUserId);
        }

        // A's audit log knows nothing about it. An audit row leaking across communities would tell one
        // community who acted in another, which is exactly the shape of thing audit rows contain.
        await using var inA = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.Empty(await inA.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ReachingAnotherCommunitysClaimRoute_Is404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();
        await ClaimAsync(scenario.TenantA, shared.Id);

        // 404, never 403 — a 403 would confirm the community exists (tenancy.md).
        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/claims/{shared.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACachedClaimStateFromOneCommunity_IsNotServedToAnother()
    {
        // The cache-key half of tenancy.md's testing section. The claim GET is cached under
        // t:{tenantId}:claim:{characterId}; a bare key would pass every other test in this file and
        // fail only this one, by telling B that A's member owns the character.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var shared = await SeedCharacterAsync();

        await ClaimAsync(scenario.TenantA, shared.Id);

        // A reads first, populating its entry. B's read must miss it entirely.
        Assert.Equal(scenario.TenantA.UserId, (await GetAsync(scenario.TenantA, shared.Id)).ClaimedByUserId);
        Assert.Null((await GetAsync(scenario.TenantB, shared.Id)).ClaimedByUserId);
    }

    private async Task<Domain.Managers.Models.Domain.Character> SeedCharacterAsync()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        return await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
    }

    private static async Task<CharacterClaimServiceModel> ClaimAsync(TenantSide side, Guid characterId)
    {
        var response = await side.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{side.Tenant.Slug}/claims",
            new ClaimCharacterViewModel { CharacterId = characterId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CharacterClaimServiceModel>())!;
    }

    private static async Task<CharacterClaimServiceModel> GetAsync(TenantSide side, Guid characterId)
    {
        var response = await side.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/claims/{characterId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CharacterClaimServiceModel>())!;
    }
}
