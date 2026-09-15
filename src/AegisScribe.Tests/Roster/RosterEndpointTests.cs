using System.Net;
using System.Net.Http.Headers;
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

// Through the endpoint: RosterController -> IRosterFacade -> IRosterBusiness -> IRosterEntryDataLayer
// -> IRosterEntryRepository -> SQL, plus the TenantMember policy, the cursor round-trip and the ETag.
// Cross-tenant behaviour is RosterIsolationTests' subject, not this file's.
[Collection("AegisScribe API")]
public class RosterEndpointTests(AegisScribeAppFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task AMemberSeesTheRosterWithTheCommunitysOwnRanks()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Read", TenantRole.Member);
        var rank = await SeedRankAsync(member.Tenant.Id, "Raider", 10, "#cba76a");
        await SeedEntryAsync(member.Tenant.Id, "Aldric", rank);

        var page = await ListAsync(member);

        var row = Assert.Single(page.Items);
        Assert.Equal("Aldric", row.CharacterName);
        Assert.Equal("Raider", row.RankName);
        Assert.Equal("#cba76a", row.RankColour);
        Assert.Equal(10, row.RankSortOrder);
        Assert.Equal(CharacterClass.Warrior, row.Class);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task TheOfficerNoteReachesAnOfficerButNeverAMember()
    {
        // The gate on a private field, asserted against RAW JSON from both sides rather than against a
        // deserialized model — a model that stopped populating the field would still deserialize fine,
        // and so would one that started leaking it.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Note Gate", TenantRole.Officer);
        var member = await TenantSide.JoinAsync(fixture, officer.Tenant, TenantRole.Member);
        await SeedEntryAsync(officer.Tenant.Id, "Aldric", rankId: null, officerNote: "Keeps missing raids");

        var asOfficer = await officer.SendAsync(HttpMethod.Get, $"/api/v1/t/{officer.Tenant.Slug}/roster");
        Assert.Equal(HttpStatusCode.OK, asOfficer.StatusCode);
        Assert.Contains("Keeps missing raids", await asOfficer.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The member sees the row and not the remark. Blanked in the SQL projection, so the note never
        // left the database on this request at all.
        var asMember = await member.SendAsync(HttpMethod.Get, $"/api/v1/t/{member.Tenant.Slug}/roster");
        var memberBody = await asMember.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, asMember.StatusCode);
        Assert.Contains("Aldric", memberBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Keeps missing raids", memberBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARosterRowCarriesBothRanks_AndNeitherDerivesFromTheOther()
    {
        // 7.5's central restriction, asserted at the API rather than only in the UI: the community's
        // own rank and the game's arrive as separate fields from separate tables, and a row can have
        // either without the other.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Both Ranks", TenantRole.Officer);
        var guild = await SeedGuildAsync(officer.Tenant.Id);
        var tenantRank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10, "#cba76a");

        // In the guild at in-game rank 3, and a Raider in this community. Unrelated facts.
        var entry = await SeedEntryAsync(officer.Tenant.Id, "Thornwake", tenantRank, guild: guild, blizzardRank: 3);
        await SetGuildRankNameAsync(officer, guild.Id, 3, "Veteran");

        var row = Assert.Single((await ListAsync(officer)).Items, r => r.Id == entry);

        Assert.Equal("Raider", row.RankName);
        Assert.Equal(3, row.BlizzardRank);
        Assert.Equal("Veteran", row.BlizzardRankName);
        Assert.Equal(guild.Name, row.GuildName);

        // The class colour is mapped API-side, so the frontend never owns a class→hex table.
        Assert.Equal("#C69B6D", row.ClassColor);
        Assert.NotEqual(default, row.LastSyncedAt);
    }

    [Fact]
    public async Task ACharacterInNoFollowedGuild_HasNoInGameRankAtAll()
    {
        // All three null together. A character the community has rostered but whose guild it does not
        // follow has no in-game standing we can honestly report.
        var officer = await TenantSide.CreateAsync(fixture, "Roster No Guild", TenantRole.Officer);
        var tenantRank = await SeedRankAsync(officer.Tenant.Id, "Raider", 10, "#cba76a");
        await SeedEntryAsync(officer.Tenant.Id, "Thornwake", tenantRank);

        var row = Assert.Single((await ListAsync(officer)).Items);

        Assert.Equal("Raider", row.RankName);
        Assert.Null(row.BlizzardRank);
        Assert.Null(row.BlizzardRankName);
        Assert.Null(row.GuildName);
    }

    [Fact]
    public async Task AnUnnamedInGameRank_ComesBackAsANumberWithNoName()
    {
        // Blizzard does not expose guild rank names, so an unnamed rank stays unnamed. Inventing one
        // would present our guess as the guild's own — the whole reason GuildRankName exists.
        var officer = await TenantSide.CreateAsync(fixture, "Roster Unnamed Rank", TenantRole.Officer);
        var guild = await SeedGuildAsync(officer.Tenant.Id);
        await SeedEntryAsync(officer.Tenant.Id, "Thornwake", null, guild: guild, blizzardRank: 5);

        var row = Assert.Single((await ListAsync(officer)).Items);

        Assert.Equal(5, row.BlizzardRank);
        Assert.Null(row.BlizzardRankName);
    }

    [Fact]
    public async Task ASortOutsideTheWhitelist_Is400()
    {
        // The value reaches an ORDER BY, so the set is closed. Refused rather than defaulted: a page
        // quietly returned in a different order is how a paging bug gets blamed on the server.
        var member = await TenantSide.CreateAsync(fixture, "Roster Sort", TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{member.Tenant.Slug}/roster?sort=byVibes");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ACursorFromOneSort_DoesNotResumeAgainstAnother()
    {
        // The sort's name rides inside the cursor for this reason: a position that means "after item
        // level 639" is meaningless in a name ordering, and seeking to it would silently skip rows.
        // A mismatch restarts the page instead.
        var member = await TenantSide.CreateAsync(fixture, "Roster Sort Cursor", TenantRole.Member);
        await SeedEntryAsync(member.Tenant.Id, "Aldric", null);
        await SeedEntryAsync(member.Tenant.Id, "Borin", null);

        var byItemLevel = await ListAsync(member, limit: 1, sort: "itemLevel");
        Assert.NotNull(byItemLevel.NextCursor);

        var replayed = await ListAsync(member, limit: 10, sort: "name", cursor: byItemLevel.NextCursor);

        // Restarted: both rows, not a page seeked into by a key from the wrong ordering.
        Assert.Equal(2, replayed.Items.Count);
    }

    [Fact]
    public async Task TheCursorWalksThePagesWithoutRepeatingARow()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Paging", TenantRole.Member);
        var rank = await SeedRankAsync(member.Tenant.Id, "Raider", 10, "#cba76a");

        await SeedEntryAsync(member.Tenant.Id, "Aldric", rank);
        await SeedEntryAsync(member.Tenant.Id, "Borin", rank);
        await SeedEntryAsync(member.Tenant.Id, "Mira", null);

        var first = await ListAsync(member, limit: 2);
        Assert.Equal(new[] { "Aldric", "Borin" }, first.Items.Select(row => row.CharacterName).ToArray());
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);

        // The cursor crosses from the ranked group into the unranked one, which is the case the
        // sentinel exists for — a naive nulls-last implementation restarts the page here.
        var second = await ListAsync(member, limit: 2, cursor: first.NextCursor);
        Assert.Equal("Mira", Assert.Single(second.Items).CharacterName);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task AGarbledCursor_RestartsThePageRatherThanFailingTheRequest()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Cursor", TenantRole.Member);
        await SeedEntryAsync(member.Tenant.Id, "Aldric", null);

        var page = await ListAsync(member, cursor: "not-a-cursor");

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task ALimitOverTheCap_IsAValidationProblem()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Limit", TenantRole.Member);

        var response = await member.SendAsync(
            HttpMethod.Get, $"/api/v1/t/{member.Tenant.Slug}/roster?limit=5000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Limit", problem!.Errors.Keys);
    }

    [Fact]
    public async Task RepeatingARequestWithIfNoneMatch_Is304()
    {
        // A 304 costs a few bytes where a full roster costs kilobytes, and a phone refreshing in the
        // background pays that difference out of the user's data allowance (api-contract.md).
        var member = await TenantSide.CreateAsync(fixture, "Roster ETag", TenantRole.Member);
        await SeedEntryAsync(member.Tenant.Id, "Aldric", null);

        var first = await member.SendAsync(HttpMethod.Get, $"/api/v1/t/{member.Tenant.Slug}/roster");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrEmpty(etag));

        using var conditional = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/t/{member.Tenant.Slug}/roster");
        conditional.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var second = await fixture.ApiClient.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task WithoutATokenTheRosterIs401()
    {
        var member = await TenantSide.CreateAsync(fixture, "Roster Anonymous", TenantRole.Member);

        var response = await fixture.ApiClient.GetAsync($"/api/v1/t/{member.Tenant.Slug}/roster");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<CursorPageServiceModel<RosterEntryServiceModel>> ListAsync(
        TenantSide side, int? limit = null, string? cursor = null, string? sort = null)
    {
        var query = new List<string>();
        if (limit is not null)
        {
            query.Add($"limit={limit}");
        }

        if (sort is not null)
        {
            query.Add($"sort={sort}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var path = $"/api/v1/t/{side.Tenant.Slug}/roster"
            + (query.Count > 0 ? $"?{string.Join('&', query)}" : string.Empty);

        var response = await side.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<CursorPageServiceModel<RosterEntryServiceModel>>(Json))!;
    }

    private async Task<Guid> SeedRankAsync(Guid tenantId, string name, int sortOrder, string colour)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var rank = new TenantRank { Id = Guid.NewGuid(), Name = name, SortOrder = sortOrder, Colour = colour };
        db.TenantRanks.Add(rank);
        await db.SaveChangesAsync();

        return rank.Id;
    }

    // The write endpoints are 7.4's, so entries are seeded directly.
    private async Task<Guid> SeedEntryAsync(
        Guid tenantId,
        string characterName,
        Guid? rankId,
        string? officerNote = null,
        Guild? guild = null,
        int? blizzardRank = null)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, characterName);

        // The in-game membership, in the GLOBAL zone — Guild and GuildMember are facts about the game,
        // shared by every community that follows them.
        if (guild is not null && blizzardRank is not null)
        {
            await using var global = await CharacterSeeding.OpenDbContextAsync(fixture);
            global.GuildMembers.Add(new GuildMember
            {
                Id = Guid.NewGuid(),
                GuildId = guild.Id,
                CharacterId = character.Id,
                BlizzardRank = blizzardRank.Value,
            });
            await global.SaveChangesAsync();
        }

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            TenantRankId = rankId,
            OfficerNote = officerNote,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        db.RosterEntries.Add(entry);

        await db.SaveChangesAsync();

        return entry.Id;
    }

    // A global Guild, plus this community's link to it. The link is what makes the in-game rank
    // visible here at all — a character's rank in a guild nobody follows is not this roster's business.
    private async Task<Guild> SeedGuildAsync(Guid tenantId)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var name = $"Guild{Guid.NewGuid():N}"[..18];

        await using (var global = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            global.Guilds.Add(new Guild
            {
                Id = Guid.NewGuid(),
                RealmId = realm.Id,
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Faction = CharacterFaction.Alliance,
                BlizzardGuildId = Random.Shared.NextInt64(1, long.MaxValue),
                LastSyncedAt = DateTimeOffset.UtcNow,
            });
            await global.SaveChangesAsync();
        }

        await using var lookup = await CharacterSeeding.OpenDbContextAsync(fixture);
        var guild = lookup.Guilds.Single(g => g.NameLower == name.ToLowerInvariant());

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        db.TenantGuilds.Add(new TenantGuild
        {
            Id = Guid.NewGuid(),
            GuildId = guild.Id,
            LinkedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return guild;
    }

    private static async Task SetGuildRankNameAsync(TenantSide side, Guid guildId, int rank, string name)
    {
        var response = await side.SendAsync(
            HttpMethod.Put,
            $"/api/v1/t/{side.Tenant.Slug}/guild-rank-names/{guildId}/{rank}",
            new SetGuildRankNameViewModel { Name = name });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
