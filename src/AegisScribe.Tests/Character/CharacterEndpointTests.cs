using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;

namespace AegisScribe.Tests.Character;

// Its own collection/AppHost for the same reason MeEndpointCollection/TenantResolutionCollection
// have one: these are unauthenticated requests against the shared IP rate-limit bucket
// (backend.md -> "The API's public edge"), so a busy shared collection could make this flaky.
[CollectionDefinition("AegisScribe API - Characters")]
public class CharacterEndpointCollection : ICollectionFixture<AegisScribeAppFixture>;

// Through the endpoint, the whole stack: CharactersController -> ICharacterFacade -> CharacterBusiness
// -> ICharacterDataLayer -> CharacterRepository -> SQL, plus the global handler's 400 mapping and the
// controller's own 404/ETag handling. The layers below the controller already have their own focused
// tests (Repository/DataLayer/Business/Facade) — this is the missing "does it actually work as a
// route" check the add-endpoint skill calls for (skills-evals, 4.6).
[Collection("AegisScribe API - Characters")]
public class CharacterEndpointTests(AegisScribeAppFixture fixture)
{
    // The API serializes enums as strings (Program.cs's JsonStringEnumConverter registration,
    // api-contract.md); HttpClient's default ReadFromJsonAsync options don't know that, so the test
    // client needs its own copy to deserialize CharacterClass/CharacterFaction/etc. correctly.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Get_ReturnsTheCharacterDetail_ForASeededCharacter()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: true);

        var response = await fixture.ApiClient.GetAsync(
            $"/api/v1/characters/{realm.Slug}/{character.Name}?region={realm.Region}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag is not null);
        var body = await response.Content.ReadFromJsonAsync<CharacterDetailServiceModel>(JsonOptions);
        Assert.Equal(character.Id, body!.Id);
        Assert.Equal(realm.Slug, body.RealmSlug);
        Assert.Single(body.Equipment);
    }

    [Fact]
    public async Task Get_MatchingIfNoneMatch_Returns304()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
        var path = $"/api/v1/characters/{realm.Slug}/{character.Name}?region={realm.Region}";

        var first = await fixture.ApiClient.GetAsync(path);
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await fixture.ApiClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownCharacter_Returns404()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        var response = await fixture.ApiClient.GetAsync(
            $"/api/v1/characters/{realm.Slug}/{CharacterSeeding.UniqueName()}?region={realm.Region}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_MissingRegion_ReturnsValidationProblem()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        var response = await fixture.ApiClient.GetAsync($"/api/v1/characters/{realm.Slug}/{character.Name}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetailsResponse>();
        // The ProblemDetails writer camelCases error keys to match the API's wire convention
        // (api-contract.md) — "Region" the C# property becomes "region" on the wire.
        Assert.Contains("region", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Search_ReturnsAPageFilteredByRealm()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var prefix = $"Ep{Guid.NewGuid():N}"[..12];
        await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: $"{prefix}Alpha");
        await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: $"{prefix}Bravo");

        var response = await fixture.ApiClient.GetAsync(
            $"/api/v1/characters?region={realm.Region}&realm={realm.Slug}&name={prefix.ToLowerInvariant()}&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPageServiceModel<CharacterSummaryServiceModel>>(JsonOptions);
        Assert.Single(page!.Items);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextCursor);
    }

    private sealed class ValidationProblemDetailsResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
