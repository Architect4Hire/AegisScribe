using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// 7.1's two-tenant test, and the only kind of test that says anything at all about isolation: a
// single-tenant suite passes every query, because there is no other community's data to leak
// (tenancy.md). Everything here goes through the endpoint, never the repository.
//
// A rank ladder is the clearest case in the repo of the "would two communities disagree" test — both
// communities below define a rank called "Raider", both are right, and neither may see the other's.
[Collection("AegisScribe API")]
public class TenantRankIsolationTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task BothCommunitiesMayDefineARaider_AndNeitherSeesTheOthers()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var aRank = await CreateAsync(scenario.TenantA, "Raider", 10, "#cba76a");
        var bRank = await CreateAsync(scenario.TenantB, "Raider", 10, "#3e9c77");
        await CreateAsync(scenario.TenantB, "Trial", 20, "#616d7e");

        var aList = await ListAsync(scenario.TenantA);
        var bList = await ListAsync(scenario.TenantB);

        // The same name in both, different rows, different colours. If the unique index were on Name
        // alone, B's create would have failed; if the query filter were missing, each list would show
        // three ranks.
        Assert.Equal(aRank.Id, Assert.Single(aList).Id);
        Assert.Equal("#cba76a", aList[0].Colour);
        Assert.Equal(2, bList.Count);
        Assert.Contains(bList, rank => rank.Id == bRank.Id && rank.Colour == "#3e9c77");
        Assert.DoesNotContain(bList, rank => rank.Id == aRank.Id);
    }

    [Fact]
    public async Task ACommunityCannotEditAnotherCommunitysRank()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var aRank = await CreateAsync(scenario.TenantA, "Raider", 10, "#cba76a");

        // B knows the id — guessing a GUID is not the leak. What B must not be able to do is act on it.
        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/ranks/{aRank.Id}",
            new UpdateRankViewModel { Name = "Bench", SortOrder = 99, Colour = "#ffffff" });

        // 404, not 403: "no such rank" and "that rank is another community's" must be
        // indistinguishable, or the response is an existence oracle for rows B cannot see.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var aAfter = Assert.Single(await ListAsync(scenario.TenantA));
        Assert.Equal("Raider", aAfter.Name);
        Assert.Equal("#cba76a", aAfter.Colour);
        Assert.Equal(10, aAfter.SortOrder);
    }

    [Fact]
    public async Task ACommunitysDeleteCannotReachAnotherCommunitysRank()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var aRank = await CreateAsync(scenario.TenantA, "Raider", 10, "#cba76a");

        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Delete, $"/api/v1/t/{scenario.TenantB.Tenant.Slug}/ranks/{aRank.Id}");

        // 204, because DELETE is idempotent (api-contract.md) and B's request looks exactly like a
        // delete of something already gone — which, from inside B's tenant, is what it is. The status
        // is not the isolation assertion; the next one is.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(aRank.Id, Assert.Single(await ListAsync(scenario.TenantA)).Id);

        // And confirmed at rest, not only through A's own (possibly cached) list.
        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, scenario.TenantA.Tenant.Id);
        Assert.True(await verify.TenantRanks.AnyAsync(rank => rank.Id == aRank.Id));
    }

    [Fact]
    public async Task ReachingAnotherCommunitysRankRoute_Is404NotForbidden()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        await CreateAsync(scenario.TenantA, "Raider", 10, "#cba76a");

        // A 403 here would confirm the community exists, which is itself a leak (tenancy.md).
        var response = await scenario.TenantB.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}/ranks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACachedLadderPopulatedByOneCommunity_IsNotServedToAnother()
    {
        // The cache-key half of tenancy.md's testing section, and the reason this feature exists as
        // the first caller of TenantScopedFacadeBase: a bare "ranks" key would pass every other test
        // in this file and fail only this one.
        var scenario = await TwoTenantFixture.CreateAsync(fixture);
        var aRank = await CreateAsync(scenario.TenantA, "Raider", 10, "#cba76a");

        // A reads first, populating t:{A}:ranks. B's read must miss that entry entirely.
        Assert.Equal(aRank.Id, Assert.Single(await ListAsync(scenario.TenantA)).Id);
        Assert.Empty(await ListAsync(scenario.TenantB));

        // Now B writes. A's cached ladder must still be A's — an invalidation that removed a shared
        // key would be just as wrong as a shared read.
        var bRank = await CreateAsync(scenario.TenantB, "Trial", 20, "#616d7e");

        Assert.Equal(aRank.Id, Assert.Single(await ListAsync(scenario.TenantA)).Id);
        Assert.Equal(bRank.Id, Assert.Single(await ListAsync(scenario.TenantB)).Id);
    }

    private static async Task<TenantRankServiceModel> CreateAsync(
        TenantSide side, string name, int sortOrder, string colour)
    {
        var response = await side.SendAsync(
            HttpMethod.Post,
            $"/api/v1/t/{side.Tenant.Slug}/ranks",
            new CreateRankViewModel { Name = name, SortOrder = sortOrder, Colour = colour });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TenantRankServiceModel>())!;
    }

    private static async Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(TenantSide side)
    {
        var response = await side.SendAsync(HttpMethod.Get, $"/api/v1/t/{side.Tenant.Slug}/ranks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<TenantRankServiceModel>>())!;
    }
}
