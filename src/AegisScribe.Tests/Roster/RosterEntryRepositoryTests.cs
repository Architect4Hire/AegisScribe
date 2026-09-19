using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Character;
using AegisScribe.Tests.Tenancy;

namespace AegisScribe.Tests.Roster;

// Repository: real SQL Server through the Aspire fixture. The keyset predicates are the reason — three
// seeks with a sentinel for unranked rows and a descending variant are exactly the kind of thing that
// translates differently (or not at all) against an in-memory provider.
[Collection("AegisScribe API")]
public class RosterEntryRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task ByRank_OrdersByRankThenName_WithUnrankedLast()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var officer = await SeedRankAsync(tenant.Id, "Officer", 10);
        var raider = await SeedRankAsync(tenant.Id, "Raider", 20);

        // Deliberately seeded out of order, and with two characters sharing a rank so the name
        // tie-break is actually exercised rather than incidentally satisfied.
        await SeedEntryAsync(tenant.Id, "Zara", raider);
        await SeedEntryAsync(tenant.Id, "Aldric", raider);
        await SeedEntryAsync(tenant.Id, "Mira", officer);
        await SeedEntryAsync(tenant.Id, "Borin", null);

        var page = await ListAsync(tenant.Id, RosterSort.Rank, take: 10);

        Assert.Equal(
            new[] { "Mira", "Aldric", "Zara", "Borin" },
            page.Select(row => row.CharacterName).ToArray());

        // The unranked row carries nulls across the whole rank group rather than a zero or an empty
        // string — the UI decides how to render "no rank", and a sentinel leaking onto the wire would
        // make that decision for it.
        var unranked = page[^1];
        Assert.Null(unranked.RankId);
        Assert.Null(unranked.RankName);
        Assert.Null(unranked.RankColour);
        Assert.Null(unranked.RankSortOrder);
    }

    [Fact]
    public async Task ByItemLevel_OrdersHighestFirst()
    {
        // Descending, because that is the only direction a roster is read in (screen S3's ↓) — and
        // because the descending keyset is a different predicate from the ascending one, so it needs
        // its own evidence.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedEntryAsync(tenant.Id, "Aldric", null, itemLevel: 610);
        await SeedEntryAsync(tenant.Id, "Borin", null, itemLevel: 639);
        await SeedEntryAsync(tenant.Id, "Mira", null, itemLevel: 624);

        var page = await ListAsync(tenant.Id, RosterSort.ItemLevel, take: 10);

        Assert.Equal(new[] { "Borin", "Mira", "Aldric" }, page.Select(row => row.CharacterName).ToArray());
    }

    [Fact]
    public async Task ByName_OrdersAlphabetically()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedEntryAsync(tenant.Id, "Zara", null);
        await SeedEntryAsync(tenant.Id, "Aldric", null);
        await SeedEntryAsync(tenant.Id, "Mira", null);

        var page = await ListAsync(tenant.Id, RosterSort.Name, take: 10);

        Assert.Equal(new[] { "Aldric", "Mira", "Zara" }, page.Select(row => row.CharacterName).ToArray());
    }

    [Fact]
    public async Task AMainAndItsAltsStayTogether_EvenWhenTheSortWouldSeparateThem()
    {
        // The reason the page is taken in MAINS rather than rows. Sorted by item level the alt belongs
        // last of all four; grouped, it has to sit directly under its main — and on the SAME page, or
        // the roster renders an orphaned ↳ row that no client can attach to anything.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var main = await SeedEntryAsync(tenant.Id, "Thornwake", null, itemLevel: 639);
        await SeedEntryAsync(tenant.Id, "Thornbite", null, itemLevel: 600, mainRosterEntryId: main);
        await SeedEntryAsync(tenant.Id, "Aldric", null, itemLevel: 630);
        await SeedEntryAsync(tenant.Id, "Borin", null, itemLevel: 620);

        // One main, and its alt comes with it — two rows from a limit of one.
        var firstPage = await ListAsync(tenant.Id, RosterSort.ItemLevel, take: 1);

        Assert.Equal(new[] { "Thornwake", "Thornbite" }, firstPage.Select(row => row.CharacterName).ToArray());
        Assert.Null(firstPage[0].MainRosterEntryId);
        Assert.Equal(main, firstPage[1].MainRosterEntryId);
    }

    [Fact]
    public async Task TheCursorResumesFromTheLastMain_WithoutRepeatingOrSkipping()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var thornwake = await SeedEntryAsync(tenant.Id, "Thornwake", null, itemLevel: 639);
        await SeedEntryAsync(tenant.Id, "Thornbite", null, itemLevel: 600, mainRosterEntryId: thornwake);
        await SeedEntryAsync(tenant.Id, "Aldric", null, itemLevel: 630);
        await SeedEntryAsync(tenant.Id, "Borin", null, itemLevel: 620);

        var first = await ListAsync(tenant.Id, RosterSort.ItemLevel, take: 1);
        var lastMain = first.Last(row => row.MainRosterEntryId is null);

        var second = await ListAsync(
            tenant.Id,
            RosterSort.ItemLevel,
            take: 10,
            // The compound key the controller mints: the primary sort value, then the name that breaks
            // ties within it. A key missing its name half loses the tie-break and the resumed page
            // starts reordering equally-geared members between reads.
            afterKey: $"{lastMain.ItemLevel}~{lastMain.CharacterName.ToLowerInvariant()}",
            afterId: lastMain.Id);

        // Neither the main already returned nor the alt that rode along with it comes back again.
        Assert.Equal(new[] { "Aldric", "Borin" }, second.Select(row => row.CharacterName).ToArray());
    }

    [Fact]
    public async Task TheOfficerNoteIsBlankedInTheProjection_NotAfterIt()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedEntryAsync(tenant.Id, "Aldric", null, officerNote: "Keeps missing raids");

        var withNote = await ListAsync(tenant.Id, RosterSort.Rank, take: 10, includeOfficerNote: true);
        Assert.Equal("Keeps missing raids", Assert.Single(withNote).OfficerNote);

        // Null for a caller whose rank does not earn it. Asserted at this layer because the blanking
        // happens in the SQL projection — the note never leaves the database on a request that had no
        // business reading it.
        var withoutNote = await ListAsync(tenant.Id, RosterSort.Rank, take: 10, includeOfficerNote: false);
        Assert.Null(Assert.Single(withoutNote).OfficerNote);
    }

    [Fact]
    public async Task EachRowCarriesItsRealmsRegion_SoTheCharacterProfileCanBeAddressed()
    {
        // Not "us" on purpose: the default seed region would pass even if the projection hard-coded it.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedEntryAsync(tenant.Id, "Aldric", null, region: "eu");

        var row = Assert.Single(await ListAsync(tenant.Id, RosterSort.Rank, take: 10));

        Assert.Equal("eu", row.Region);
        Assert.False(string.IsNullOrEmpty(row.RealmSlug));
    }

    [Fact]
    public async Task CountByRank_CountsOnlyTheAmbientCommunitysHolders()
    {
        // The count that decides whether a rank delete is refused. It has to be query-filtered, or one
        // community's roster would make another community's rank undeletable — a cross-tenant denial
        // of service dressed up as a safety check.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);

        var aRank = await SeedRankAsync(tenantA.Id, "Raider", 10);
        await SeedEntryAsync(tenantA.Id, "Aldric", aRank);
        await SeedEntryAsync(tenantA.Id, "Borin", aRank);

        await using (var aDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantA.Id))
        {
            Assert.Equal(2, await RepositoryFor(aDb, tenantA.Id).CountByRankAsync(aRank, CancellationToken.None));
        }

        await using var bDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantB.Id);
        Assert.Equal(0, await RepositoryFor(bDb, tenantB.Id).CountByRankAsync(aRank, CancellationToken.None));
    }

    [Fact]
    public async Task HasAlts_SeesOnlyTheAmbientCommunitysAlts()
    {
        // The read behind the rule that an entry which already has alts may not become one. It has to
        // be query-filtered, or another community's roster could make an entry undemotable here — and,
        // worse, a link could be refused for a reason the caller cannot see.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);

        var mainInA = await SeedEntryAsync(tenantA.Id, "Thornwake", null);
        await SeedEntryAsync(tenantA.Id, "Thornbite", null, mainRosterEntryId: mainInA);

        await using (var aDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantA.Id))
        {
            Assert.True(await RepositoryFor(aDb, tenantA.Id).HasAltsAsync(mainInA, CancellationToken.None));
        }

        await using var bDb = await TenantSeeding.OpenDbContextAsync(fixture, tenantB.Id);
        Assert.False(await RepositoryFor(bDb, tenantB.Id).HasAltsAsync(mainInA, CancellationToken.None));
    }

    [Fact]
    public async Task ARankCarryingAnotherCommunitysTenantId_IsRefusedAndPersistsNothing()
    {
        // tenancy.md's third required assertion, at the layer below the endpoint: a write from A cannot
        // set a row's TenantId to B. No request shape can express it — no roster ViewModel has a
        // TenantId field — so this pins the interceptor, and that RosterEntry actually travels that
        // path against real SQL Server.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);

        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantA.Id);
        db.RosterEntries.Add(new RosterEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            CharacterId = character.Id,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<TenantStampMismatchException>(() => db.SaveChangesAsync());
    }

    // The repository takes ITenantContext for one thing only — naming the community TryLinkAltAsync
    // locks. Every read below still leans on the ambient query filter, which is what the isolation
    // assertions in this file are actually testing, so this must match the tenant the DbContext was
    // opened for or those assertions would be testing the wrong thing.
    private static RosterEntryRepository RepositoryFor(AegisScribeDbContext db, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        return new RosterEntryRepository(db, tenantContext);
    }

    private async Task<IReadOnlyList<RosterEntryServiceModel>> ListAsync(
        Guid tenantId,
        RosterSort sort,
        int take,
        string? afterKey = null,
        Guid? afterId = null,
        bool includeOfficerNote = false)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var repository = RepositoryFor(db, tenantId);

        var mainIds = await repository.ListMainIdsAsync(sort, afterKey, afterId, take, CancellationToken.None);

        return await repository.ListGroupsAsync(mainIds, sort, includeOfficerNote, CancellationToken.None);
    }

    private async Task<Guid> SeedRankAsync(Guid tenantId, string name, int sortOrder)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var rank = new TenantRank { Id = Guid.NewGuid(), Name = name, SortOrder = sortOrder, Colour = "#cba76a" };
        db.TenantRanks.Add(rank);
        await db.SaveChangesAsync();

        return rank.Id;
    }

    // Character names are given explicitly here, unlike the isolation tests — the ordering assertions
    // are the subject, so the sort key cannot be random.
    private async Task<Guid> SeedEntryAsync(
        Guid tenantId,
        string characterName,
        Guid? rankId,
        Guid? mainRosterEntryId = null,
        int itemLevel = 600,
        string? officerNote = null,
        string? region = null)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region);
        var character = await CharacterSeeding.CreateCharacterAsync(
            fixture, realm.Id, characterName, itemLevel: itemLevel);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);
        var entry = new RosterEntry
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            TenantRankId = rankId,
            MainRosterEntryId = mainRosterEntryId,
            OfficerNote = officerNote,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        db.RosterEntries.Add(entry);

        await db.SaveChangesAsync();

        return entry.Id;
    }
}
