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

// Through the endpoint: TenantRanksController -> ITenantRankFacade -> ITenantRankBusiness ->
// ITenantRankDataLayer -> ITenantRankRepository -> SQL, plus the tenant policies and the global
// exception handler. Cross-tenant behaviour is TenantRankIsolationTests' subject, not this file's.
[Collection("AegisScribe API")]
public class TenantRankEndpointTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task OfficerCanCreateListUpdateAndDeleteARank()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank CRUD", TenantRole.Officer);
        var route = $"/api/v1/t/{officer.Tenant.Slug}/ranks";

        var created = await officer.SendAsync(
            HttpMethod.Post, route, new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var rank = (await created.Content.ReadFromJsonAsync<TenantRankServiceModel>())!;
        Assert.Equal("Raider", rank.Name);
        Assert.NotEqual(Guid.Empty, rank.Id);

        var afterCreate = await ListAsync(officer);
        Assert.Equal("#cba76a", Assert.Single(afterCreate).Colour);

        var updated = await officer.SendAsync(
            HttpMethod.Put,
            $"{route}/{rank.Id}",
            new UpdateRankViewModel { Name = "Core Raider", SortOrder = 5, Colour = "#3e9c77" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // Read back through the endpoint rather than trusting the PUT's own body — this is also what
        // proves the facade invalidated its cached list rather than serving the pre-edit ladder.
        var afterUpdate = Assert.Single(await ListAsync(officer));
        Assert.Equal("Core Raider", afterUpdate.Name);
        Assert.Equal(5, afterUpdate.SortOrder);
        Assert.Equal("#3e9c77", afterUpdate.Colour);

        var deleted = await officer.SendAsync(HttpMethod.Delete, $"{route}/{rank.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty(await ListAsync(officer));
    }

    [Fact]
    public async Task DeletingARankThatIsAlreadyGone_IsStill204()
    {
        // api-contract.md: DELETE is idempotent, and the client's intent is satisfied either way. A
        // mobile client that retries after a dropped response must not see its own success as an error.
        var officer = await TenantSide.CreateAsync(fixture, "Rank Delete", TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{officer.Tenant.Slug}/ranks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdatingARankThatDoesNotExist_Is404()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank Update 404", TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{officer.Tenant.Slug}/ranks/{Guid.NewGuid()}",
            new UpdateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletingARankThatRosterEntriesStillHold_Is409WithTheStableTypeUri()
    {
        // 7.2: the ladder is load-bearing once entries point at it. Refusing here is what keeps a
        // tidy-up from silently un-ranking everyone who held the rank.
        var officer = await TenantSide.CreateAsync(fixture, "Rank In Use", TenantRole.Officer);
        var route = $"/api/v1/t/{officer.Tenant.Slug}/ranks";

        var created = await officer.SendAsync(
            HttpMethod.Post, route, new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });
        var rank = (await created.Content.ReadFromJsonAsync<TenantRankServiceModel>())!;

        await SeedRosterEntryAsync(officer.Tenant.Id, rank.Id);

        var response = await officer.SendAsync(HttpMethod.Delete, $"{route}/{rank.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // A different `type` from tenant-rank-name-taken, because a client branches on the difference:
        // one sends you back to the name field, the other to the roster.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/tenant-rank-in-use", problem!.Type);

        // And the rank is still there — a refused delete that deleted anyway would be the worst of
        // both outcomes.
        var ranks = await ListAsync(officer);
        Assert.Contains(ranks, r => r.Id == rank.Id);
    }

    // Seeds through the DbContext because the roster write endpoints are 7.4's.
    private async Task SeedRosterEntryAsync(Guid tenantId, Guid rankId)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        db.RosterEntries.Add(new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            TenantRankId = rankId,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    [Theory]
    // Not a formatting complaint: the value is written into the rank pill's --rank-color custom
    // property, so anything but exactly #rrggbb is CSS injected through a settings form.
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#ff00ff; background: url(https://example.invalid/leak)")]
    [InlineData("")]
    public async Task AColourThatIsNotSixHexDigits_IsAValidationProblemOnTheColourField(string colour)
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank Colour", TenantRole.Officer);

        var response = await officer.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{officer.Tenant.Slug}/ranks",
            new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = colour });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(CreateRankViewModel.Colour), problem!.Errors.Keys);
    }

    [Fact]
    public async Task ASecondRankWithTheSameName_Is409WithTheStableTypeUri()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank Duplicate", TenantRole.Officer);
        var route = $"/api/v1/t/{officer.Tenant.Slug}/ranks";
        var body = new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" };

        Assert.Equal(HttpStatusCode.OK, (await officer.SendAsync(HttpMethod.Post, route, body)).StatusCode);

        // Case-insensitive, because "Raider" and "raider" are the same rank to the people reading the
        // roster — and the unique index agrees.
        var duplicate = await officer.SendAsync(
            HttpMethod.Post, route, new CreateRankViewModel { Name = "raider", SortOrder = 20, Colour = "#616d7e" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The `type` URI is the contract and may never be repurposed (api-contract.md) — the client
        // branches on it to show the message against the name field.
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://api.aegisscribe.com/problems/tenant-rank-name-taken", problem!.Type);
    }

    [Fact]
    public async Task AMemberMayReadTheLadderButNotChangeIt()
    {
        var member = await TenantSide.CreateAsync(fixture, "Rank Member", TenantRole.Member);
        var route = $"/api/v1/t/{member.Tenant.Slug}/ranks";

        // Reads are TenantMember: the roster renders a rank pill against every character, so the
        // ladder is not privileged information inside the community.
        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync(HttpMethod.Get, route)).StatusCode);

        var create = await member.SendAsync(
            HttpMethod.Post, route, new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var update = await member.SendAsync(
            HttpMethod.Put,
            $"{route}/{Guid.NewGuid()}",
            new UpdateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var delete = await member.SendAsync(HttpMethod.Delete, $"{route}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_EveryRouteIs401()
    {
        var officer = await TenantSide.CreateAsync(fixture, "Rank Anonymous", TenantRole.Officer);
        var route = $"/api/v1/t/{officer.Tenant.Slug}/ranks";

        var list = await fixture.ApiClient.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);

        var create = await fixture.ApiClient.PostAsJsonAsync(
            route, new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    private static async Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/ranks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<TenantRankServiceModel>>())!;
    }
}
