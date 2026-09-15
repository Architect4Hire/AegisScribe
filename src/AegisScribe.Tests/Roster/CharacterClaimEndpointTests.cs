using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using AegisScribe.Tests.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.Tests.Roster;

// Through the endpoint: CharacterClaimsController -> ICharacterClaimFacade -> ICharacterClaimBusiness
// -> ICharacterClaimDataLayer -> repositories -> SQL, plus both tenant policies and the global
// exception handler. Cross-tenant behaviour is CharacterClaimIsolationTests' subject.
//
// Most cases need TWO people in ONE community, which is a different axis from the two-tenant fixture —
// "you may not release somebody else's claim" needs a somebody else.
[Collection("AegisScribe API")]
public class CharacterClaimEndpointTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task AMemberCanClaimACharacterAndSeeItBackAsTheirs()
    {
        var member = await TenantSide.CreateAsync(fixture, "Claim Self", TenantRole.Member);
        var character = await SeedCharacterAsync();

        var claim = await ClaimAsync(member, character.Id);

        Assert.Equal(character.Id, claim.CharacterId);
        Assert.Equal(member.UserId, claim.ClaimedByUserId);
        Assert.Equal("Gateway Test User", claim.ClaimedByDisplayName);
        Assert.NotNull(claim.ClaimedAt);

        var read = await GetAsync(member, character.Id);
        Assert.Equal(member.UserId, read.ClaimedByUserId);
    }

    [Fact]
    public async Task AnUnclaimedCharacterReadsAs200WithNulls_NotA404()
    {
        // A 404 would leave the client unable to tell "nobody has claimed this" from "no such
        // character", and the first is the normal case the Claim button renders from.
        var member = await TenantSide.CreateAsync(fixture, "Claim Unclaimed", TenantRole.Member);
        var character = await SeedCharacterAsync();

        var read = await GetAsync(member, character.Id);

        Assert.Equal(character.Id, read.CharacterId);
        Assert.Null(read.ClaimedByUserId);
        Assert.Null(read.ClaimedByDisplayName);
        Assert.Null(read.ClaimedAt);
    }

    [Fact]
    public async Task ClaimingACharacterYouAlreadyHold_Is200NotAConflict()
    {
        // A mobile client whose response was dropped retries the POST. That retry must not surface as
        // a 409 — the intent is already satisfied.
        var member = await TenantSide.CreateAsync(fixture, "Claim Retry", TenantRole.Member);
        var character = await SeedCharacterAsync();

        var first = await ClaimAsync(member, character.Id);
        var second = await ClaimAsync(member, character.Id);

        Assert.Equal(member.UserId, second.ClaimedByUserId);
        Assert.Equal(first.ClaimedAt, second.ClaimedAt);
    }

    [Fact]
    public async Task ASecondMemberClaimingTheSameCharacter_Is409NamingTheHolder()
    {
        var holder = await TenantSide.CreateAsync(fixture, "Claim Conflict", TenantRole.Member);
        var other = await TenantSide.JoinAsync(fixture, holder.Tenant, TenantRole.Member);
        var character = await SeedCharacterAsync();

        await ClaimAsync(holder, character.Id);

        var response = await other.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{other.Tenant.Slug}/claims",
            new ClaimCharacterViewModel { CharacterId = character.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/character-already-claimed", problem!.Type);

        // The extension member the conflict dialog renders, so it needn't re-read the claim to name
        // the holder. A promise for the life of v1 (api-contract.md).
        var displayName = Assert.Contains("claimedByDisplayName", problem.Extensions);
        Assert.Equal("Gateway Test User", displayName?.ToString());

        // Refused, not overwritten — the original holder still has it.
        Assert.Equal(holder.UserId, (await GetAsync(holder, character.Id)).ClaimedByUserId);
    }

    [Fact]
    public async Task AMemberCanReleaseTheirOwnClaim()
    {
        var member = await TenantSide.CreateAsync(fixture, "Claim Release", TenantRole.Member);
        var character = await SeedCharacterAsync();
        await ClaimAsync(member, character.Id);

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{member.Tenant.Slug}/claims/{character.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await GetAsync(member, character.Id)).ClaimedByUserId);
    }

    [Fact]
    public async Task ReleasingSomebodyElsesClaim_Is403AndLeavesItAlone()
    {
        // The add-endpoint skill's canonical resource-authorization case: a rule in Business, not a
        // policy, because answering it means reading the row first.
        var holder = await TenantSide.CreateAsync(fixture, "Claim Not Yours", TenantRole.Member);
        var other = await TenantSide.JoinAsync(fixture, holder.Tenant, TenantRole.Member);
        var character = await SeedCharacterAsync();
        await ClaimAsync(holder, character.Id);

        var response = await other.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{other.Tenant.Slug}/claims/{character.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(holder.UserId, (await GetAsync(holder, character.Id)).ClaimedByUserId);
    }

    [Fact]
    public async Task ReleasingAClaimThatIsNotThere_Is204()
    {
        var member = await TenantSide.CreateAsync(fixture, "Claim Release Empty", TenantRole.Member);
        var character = await SeedCharacterAsync();

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{member.Tenant.Slug}/claims/{character.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AnOfficerCanClearAnotherMembersClaim_AndItIsAudited()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Claim Clear", TenantRole.Officer);
        var holder = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);
        var character = await SeedCharacterAsync();
        await ClaimAsync(holder, character.Id);

        var response = await officer.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{officer.Tenant.Slug}/claims/{character.Id}/holder");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Freed, not reassigned: the officer does not end up holding it.
        Assert.Null((await GetAsync(officer, character.Id)).ClaimedByUserId);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        var entry = Assert.Single(db.AuditLogs.Where(a => a.TargetId == character.Id).ToList());

        Assert.Equal(AuditAction.CharacterClaimCleared, entry.Action);
        Assert.Equal(officer.UserId, entry.ActorUserId);
        Assert.Equal(holder.UserId, entry.SubjectUserId);
        Assert.Equal(nameof(CharacterClaim), entry.TargetType);
        Assert.Equal("Unclaimed", entry.After);
    }

    [Fact]
    public async Task ClearingWhenThereIsNoClaim_Is204AndWritesNoAuditRow()
    {
        // An audit table that recorded things which did not happen would be worse than one with gaps.
        var officer = await TenantSide.CreateAsync(fixture, "Claim Clear Empty", TenantRole.Officer);
        var character = await SeedCharacterAsync();

        var response = await officer.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{officer.Tenant.Slug}/claims/{character.Id}/holder");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, officer.Tenant.Id);
        Assert.Empty(db.AuditLogs.Where(a => a.TargetId == character.Id).ToList());
    }

    [Fact]
    public async Task AMemberCannotClearAHolder()
    {
        var member = await TenantSide.CreateAsync(fixture, "Claim Clear Policy", TenantRole.Member);
        var character = await SeedCharacterAsync();

        var response = await member.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{member.Tenant.Slug}/claims/{character.Id}/holder");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ClaimingACharacterThatDoesNotExist_Is404()
    {
        // Without the existence check the foreign key would make this a 500.
        var member = await TenantSide.CreateAsync(fixture, "Claim Unknown", TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{member.Tenant.Slug}/claims",
            new ClaimCharacterViewModel { CharacterId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutATokenEveryClaimRouteIs401()
    {
        var member = await TenantSide.CreateAsync(fixture, "Claim Anonymous", TenantRole.Member);
        var character = await SeedCharacterAsync();
        var route = $"/api/v1/t/{member.Tenant.Slug}/claims";

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.GetAsync($"{route}/{character.Id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.PostAsJsonAsync(
                route, new ClaimCharacterViewModel { CharacterId = character.Id })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.ApiClient.DeleteAsync($"{route}/{character.Id}")).StatusCode);
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
