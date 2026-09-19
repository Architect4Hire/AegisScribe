using System.Globalization;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

// ITenantContext is here for one reason only: TryLinkAltAsync has to name the community it locks, and
// every other query in this file leans on the ambient query filter instead. It is never used to build
// a WHERE by hand.
public class RosterEntryRepository(AegisScribeDbContext db, ITenantContext tenantContext)
    : IRosterEntryRepository
{
    // Where an unranked entry sorts under RosterSort.Rank. A sentinel rather than SQL's NULL ordering,
    // so every term of the keyset comparison is non-null.
    private const int UnrankedOrder = int.MaxValue;

    public async Task<IReadOnlyList<Guid>> ListMainIdsAsync(
        RosterSort sort, string? afterKey, Guid? afterId, int take, CancellationToken ct)
    {
        // Mains only. Their alts are fetched by ListGroupsAsync and ride along, which is what keeps a
        // group whole across a page boundary.
        var mains = db.RosterEntries.AsNoTracking().Where(entry => entry.MainRosterEntryId == null);

        // One keyset per sort, written out rather than abstracted over: three explicit predicates are
        // much easier to verify against their own ORDER BY, and the ORDER BY agreeing with the
        // predicate is the whole correctness condition of a cursor.
        return sort switch
        {
            RosterSort.Name => await ByNameAsync(mains, afterKey, afterId, take, ct),
            RosterSort.ItemLevel => await ByItemLevelAsync(mains, afterKey, afterId, take, ct),
            _ => await ByRankAsync(mains, afterKey, afterId, take, ct),
        };
    }

    // Every sort breaks ties by character name before falling back to the id: a roster whose
    // equally-ranked members reorder themselves between reads looks broken, and an id tie-break alone
    // is a random GUID ordering. The id stays underneath because two characters on different realms
    // can share a name, and a total order is what makes a keyset resumable at all.
    //
    // So the compound sorts carry a two-part key, "{primary}~{nameLower}". A malformed one restarts
    // the page rather than failing the request: a cursor is opaque but still client-supplied
    // (api-contract.md).
    private const char KeySeparator = '~';

    private static (int? Primary, string? Name) SplitKey(string? afterKey)
    {
        if (afterKey is null)
        {
            return (null, null);
        }

        var parts = afterKey.Split(KeySeparator, 2);

        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var primary)
                ? (primary, parts[1])
                : (null, null);
    }

    private static async Task<IReadOnlyList<Guid>> ByRankAsync(
        IQueryable<RosterEntry> mains, string? afterKey, Guid? afterId, int take, CancellationToken ct)
    {
        var (afterRank, afterName) = SplitKey(afterKey);

        var keyed = mains.Select(entry => new
        {
            entry.Id,
            Rank = entry.TenantRank == null ? UnrankedOrder : entry.TenantRank.SortOrder,
            Name = entry.Character.NameLower,
        });

        if (afterRank is not null && afterName is not null && afterId is not null)
        {
            keyed = keyed.Where(row =>
                row.Rank > afterRank.Value
                || (row.Rank == afterRank.Value && row.Name.CompareTo(afterName) > 0)
                || (row.Rank == afterRank.Value && row.Name == afterName && row.Id.CompareTo(afterId.Value) > 0));
        }

        return await keyed
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Take(take)
            .Select(row => row.Id)
            .ToListAsync(ct);
    }

    private static async Task<IReadOnlyList<Guid>> ByItemLevelAsync(
        IQueryable<RosterEntry> mains, string? afterKey, Guid? afterId, int take, CancellationToken ct)
    {
        var (afterItemLevel, afterName) = SplitKey(afterKey);

        var keyed = mains.Select(entry => new
        {
            entry.Id,
            ItemLevel = entry.Character.ItemLevel,
            Name = entry.Character.NameLower,
        });

        // Item level descends, so its half of the comparison flips with it. Name and id still ascend.
        if (afterItemLevel is not null && afterName is not null && afterId is not null)
        {
            keyed = keyed.Where(row =>
                row.ItemLevel < afterItemLevel.Value
                || (row.ItemLevel == afterItemLevel.Value && row.Name.CompareTo(afterName) > 0)
                || (row.ItemLevel == afterItemLevel.Value && row.Name == afterName && row.Id.CompareTo(afterId.Value) > 0));
        }

        return await keyed
            .OrderByDescending(row => row.ItemLevel)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Take(take)
            .Select(row => row.Id)
            .ToListAsync(ct);
    }

    private static async Task<IReadOnlyList<Guid>> ByNameAsync(
        IQueryable<RosterEntry> mains, string? afterKey, Guid? afterId, int take, CancellationToken ct)
    {
        var keyed = mains.Select(entry => new { entry.Id, Key = entry.Character.NameLower });

        if (afterKey is not null && afterId is not null)
        {
            keyed = keyed.Where(row =>
                row.Key.CompareTo(afterKey) > 0
                || (row.Key == afterKey && row.Id.CompareTo(afterId.Value) > 0));
        }

        return await keyed
            .OrderBy(row => row.Key)
            .ThenBy(row => row.Id)
            .Take(take)
            .Select(row => row.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RosterEntryServiceModel>> ListGroupsAsync(
        IReadOnlyList<Guid> mainIds, RosterSort sort, bool includeOfficerNote, CancellationToken ct)
    {
        if (mainIds.Count == 0)
        {
            return [];
        }

        var rows = await db.RosterEntries
            .AsNoTracking()
            // The mains themselves and everything that calls them a main. Bounded by the page.
            .Where(entry => mainIds.Contains(entry.Id)
                || (entry.MainRosterEntryId != null && mainIds.Contains(entry.MainRosterEntryId.Value)))
            .Select(entry => new RosterRow
            {
                // Carried alongside the model so the rank NAME can be resolved after materialization.
                // Not on the wire — the guild's name is, which is what the column needs.
                GuildId = db.GuildMembers
                    .Where(member => member.CharacterId == entry.CharacterId
                        && db.TenantGuilds.Any(link => link.GuildId == member.GuildId))
                    .OrderBy(member => member.Guild.Name)
                    .Select(member => (Guid?)member.GuildId)
                    .FirstOrDefault(),
                Model = new RosterEntryServiceModel
                {
                    Id = entry.Id,
                    CharacterId = entry.CharacterId,
                    CharacterName = entry.Character.Name,
                    Region = entry.Character.Realm.Region,
                    RealmSlug = entry.Character.Realm.Slug,
                    Class = entry.Character.Class,
                    Level = entry.Character.Level,
                    ItemLevel = entry.Character.ItemLevel,
                    LastSyncedAt = entry.Character.LastSyncedAt,
                    // Restricted to guilds THIS community follows, through the tenant-scoped link
                    // rather than the global Guild table. Deterministic by guild name when a community
                    // follows several, so the column never silently means a different guild between
                    // two reads.
                    BlizzardRank = db.GuildMembers
                        .Where(member => member.CharacterId == entry.CharacterId
                            && db.TenantGuilds.Any(link => link.GuildId == member.GuildId))
                        .OrderBy(member => member.Guild.Name)
                        .Select(member => (int?)member.BlizzardRank)
                        .FirstOrDefault(),
                    GuildName = db.GuildMembers
                        .Where(member => member.CharacterId == entry.CharacterId
                            && db.TenantGuilds.Any(link => link.GuildId == member.GuildId))
                        .OrderBy(member => member.Guild.Name)
                        .Select(member => member.Guild.Name)
                        .FirstOrDefault(),
                    RankId = entry.TenantRankId,
                    RankName = entry.TenantRank == null ? null : entry.TenantRank.Name,
                    RankColour = entry.TenantRank == null ? null : entry.TenantRank.Colour,
                    RankSortOrder = entry.TenantRank == null ? (int?)null : entry.TenantRank.SortOrder,
                    MainRosterEntryId = entry.MainRosterEntryId,
                    // Joined through the tenant-scoped CharacterClaims table so a claim held in
                    // another community cannot appear here. DisplayName only — a member's email is not
                    // a fellow member's to see.
                    ClaimedByUserId = db.CharacterClaims
                        .Where(claim => claim.CharacterId == entry.CharacterId)
                        .Select(claim => claim.UserId)
                        .FirstOrDefault(),
                    ClaimedByDisplayName = db.CharacterClaims
                        .Where(claim => claim.CharacterId == entry.CharacterId)
                        .Join(db.Users, claim => claim.UserId, user => user.Id, (_, user) => user.DisplayName)
                        .FirstOrDefault(),
                    // Blanked in the projection rather than after it, so an officer-private note never
                    // leaves the database on a request that had no business reading it.
                    OfficerNote = includeOfficerNote ? entry.OfficerNote : null,
                    JoinedAt = entry.JoinedAt,
                },
            })
            .ToListAsync(ct);

        await ApplyDerivedAsync(rows, ct);

        return Arrange([.. rows.Select(row => row.Model)], mainIds, sort);
    }

    // The two fields SQL cannot produce, applied over one bounded page.
    private async Task ApplyDerivedAsync(List<RosterRow> rows, CancellationToken ct)
    {
        // The single class→colour mapping lives in CharacterMappers, and a C# switch cannot run inside
        // a SQL projection — so it runs here rather than becoming a second copy.
        foreach (var row in rows)
        {
            row.Model.ClassColor = CharacterMappers.ClassColorHex(row.Model.Class);
        }

        // One extra query rather than a correlated subquery per row: the rank NAME depends on the
        // guild the rank came from, itself the result of a subquery, and nesting those is unreadable
        // for no gain at this size.
        var wanted = rows
            .Where(row => row.GuildId is not null && row.Model.BlizzardRank is not null)
            .Select(row => row.GuildId!.Value)
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return;
        }

        var names = await db.GuildRankNames
            .AsNoTracking()
            .Where(name => wanted.Contains(name.GuildId))
            .Select(name => new { name.GuildId, name.Rank, name.Name })
            .ToListAsync(ct);

        var byKey = names.ToDictionary(name => (name.GuildId, name.Rank), name => name.Name);

        foreach (var row in rows.Where(row => row.GuildId is not null && row.Model.BlizzardRank is not null))
        {
            // Left null when nobody has named that rank; the UI shows the bare number. Blizzard does
            // not expose guild rank names, so inventing one would present our guess as the guild's own.
            row.Model.BlizzardRankName = byKey.GetValueOrDefault((row.GuildId!.Value, row.Model.BlizzardRank!.Value));
        }
    }

    // The wire model plus the one field that stays behind: the guild the in-game rank came from, which
    // the rank-name lookup needs and the client does not.
    private sealed class RosterRow
    {
        public required Guid? GuildId { get; init; }
        public required RosterEntryServiceModel Model { get; init; }
    }

    // Each main in the page's order, then its alts by the same key. Done here rather than in SQL
    // because the input is one bounded page and the alternative is a window function whose ordering
    // would have to be kept in step with ListMainIdsAsync by hand.
    private static List<RosterEntryServiceModel> Arrange(
        List<RosterEntryServiceModel> rows, IReadOnlyList<Guid> mainIds, RosterSort sort)
    {
        var altsByMain = rows
            .Where(row => row.MainRosterEntryId is not null)
            .GroupBy(row => row.MainRosterEntryId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(WithinGroup(sort)).ThenBy(r => r.Id).ToList());

        var byId = rows.Where(row => row.MainRosterEntryId is null).ToDictionary(row => row.Id);
        var arranged = new List<RosterEntryServiceModel>(rows.Count);

        foreach (var mainId in mainIds)
        {
            if (!byId.TryGetValue(mainId, out var main))
            {
                // The main was removed between the two queries. Its alts are no longer a group, so the
                // page drops them rather than emitting ↳ rows with nothing above them.
                continue;
            }

            arranged.Add(main);

            if (altsByMain.TryGetValue(mainId, out var alts))
            {
                arranged.AddRange(alts);
            }
        }

        return arranged;
    }

    private static Func<RosterEntryServiceModel, IComparable> WithinGroup(RosterSort sort) => sort switch
    {
        RosterSort.Name => row => row.CharacterName,
        RosterSort.ItemLevel => row => -row.ItemLevel,
        _ => row => row.RankSortOrder ?? UnrankedOrder,
    };

    public Task<int> CountByRankAsync(Guid rankId, CancellationToken ct) =>
        db.RosterEntries.CountAsync(entry => entry.TenantRankId == rankId, ct);

    public Task<int> CountAsync(CancellationToken ct) => db.RosterEntries.CountAsync(ct);

    // Tracked: the callers mutate or remove what comes back.
    public Task<RosterEntry?> FindEntityAsync(Guid rosterEntryId, CancellationToken ct) =>
        db.RosterEntries.FirstOrDefaultAsync(entry => entry.Id == rosterEntryId, ct);

    public Task<bool> HasAltsAsync(Guid rosterEntryId, CancellationToken ct) =>
        db.RosterEntries.AnyAsync(entry => entry.MainRosterEntryId == rosterEntryId, ct);

    public Task<int> CountAltsAsync(Guid rosterEntryId, CancellationToken ct) =>
        db.RosterEntries.CountAsync(entry => entry.MainRosterEntryId == rosterEntryId, ct);

    public Task<bool> RankExistsAsync(Guid rankId, CancellationToken ct) =>
        db.TenantRanks.AnyAsync(rank => rank.Id == rankId, ct);

    public Task<bool> IsOnRosterAsync(Guid characterId, CancellationToken ct) =>
        db.RosterEntries.AnyAsync(entry => entry.CharacterId == characterId, ct);

    public async Task<IReadOnlyList<Guid>> ListRosteredCharacterIdsAsync(
        IReadOnlyList<Guid> characterIds, CancellationToken ct)
    {
        if (characterIds.Count == 0)
        {
            return [];
        }

        return await db.RosterEntries
            .AsNoTracking()
            .Where(entry => characterIds.Contains(entry.CharacterId))
            .Select(entry => entry.CharacterId)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<UnaffiliatedRosterEntryServiceModel> Entries, int Total)>
        ListNotInAnyLinkedGuildAsync(int take, CancellationToken ct)
    {
        // NOT EXISTS over the guilds this community follows, through TenantGuilds rather than the
        // global Guilds table — same join the roster projection uses for GuildName, and for the same
        // reason: membership of a guild nobody here follows is not this community's business.
        var unaffiliated = db.RosterEntries
            .AsNoTracking()
            .Where(entry => !db.GuildMembers.Any(member =>
                member.CharacterId == entry.CharacterId
                && db.TenantGuilds.Any(link => link.GuildId == member.GuildId)));

        var total = await unaffiliated.CountAsync(ct);

        // Newest first: the rows most likely to be a surprise are the ones added since the officer
        // last looked.
        var entries = await unaffiliated
            .OrderByDescending(entry => entry.JoinedAt)
            .ThenBy(entry => entry.Id)
            .Take(take)
            .Select(entry => new UnaffiliatedRosterEntryServiceModel
            {
                RosterEntryId = entry.Id,
                CharacterName = entry.Character.Name,
                RealmSlug = entry.Character.Realm.Slug,
            })
            .ToListAsync(ct);

        return (entries, total);
    }

    public async Task RemoveEntriesForCharactersAsync(IReadOnlyList<Guid> characterIds, CancellationToken ct)
    {
        if (characterIds.Count == 0)
        {
            return;
        }

        // Both statements carry the ambient query filter, so this can only ever reach rows in the
        // resolved community — the departing member's entries next door are not this operation's.
        var doomed = await db.RosterEntries
            .AsNoTracking()
            .Where(entry => characterIds.Contains(entry.CharacterId))
            .Select(entry => entry.Id)
            .ToListAsync(ct);

        if (doomed.Count == 0)
        {
            return;
        }

        // Detach before delete, not after: the alt FK is Restrict, and an entry being removed may be
        // another member's main.
        await db.RosterEntries
            .Where(entry => entry.MainRosterEntryId != null && doomed.Contains(entry.MainRosterEntryId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.MainRosterEntryId, (Guid?)null), ct);

        await db.RosterEntries.Where(entry => doomed.Contains(entry.Id)).ExecuteDeleteAsync(ct);
    }

    public Task AddAsync(RosterEntry entry, CancellationToken ct)
    {
        db.RosterEntries.Add(entry);

        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyList<RosterEntry> entries, CancellationToken ct)
    {
        db.RosterEntries.AddRange(entries);

        return Task.CompletedTask;
    }

    public async Task SetRankAsync(Guid rosterEntryId, Guid? tenantRankId, CancellationToken ct) =>
        await db.RosterEntries
            .Where(entry => entry.Id == rosterEntryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.TenantRankId, tenantRankId), ct);

    public async Task SetOfficerNoteAsync(Guid rosterEntryId, string? officerNote, CancellationToken ct) =>
        await db.RosterEntries
            .Where(entry => entry.Id == rosterEntryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.OfficerNote, officerNote), ct);

    public Task RemoveAsync(RosterEntry entry, CancellationToken ct)
    {
        db.RosterEntries.Remove(entry);

        return Task.CompletedTask;
    }

    public async Task<bool> TryLinkAltAsync(Guid rosterEntryId, Guid mainRosterEntryId, CancellationToken ct)
    {
        // Serialise first, decide second. Rule 4 below asks "is anything pointing at this entry",
        // which is a question about a SET — and a racing A→B / C→A pair each reads a set the other is
        // about to change while writing a different row. SQL Server evaluates both predicates before
        // either takes its exclusive lock, so both can pass and leave A an alt AND a main at once.
        //
        // That window was NOT reproducible here: LinkingIntoAndOutOfTheSameEntryAtOnce and
        // TwoOppositeLinksRacing both pass without this line. It is structurally identical to the one
        // that WAS reproducible on the last-owner rule, though, and a race not caught in a handful of
        // runs is weak evidence about a window measured in microseconds. See TenantLocks.
        await db.LockCommunityAsync(tenantContext.TenantId, ct);

        // One UPDATE, no prior SELECT deciding anything. Both depth rules are in the WHERE, so the
        // check and the write are one statement — same shape as SyncBudgetRepository.TryConsumeAsync.
        //
        // The lock above can deadlock under contention; SQL Server's error 1205 is transient, so the
        // execution strategy wrapping this retries the whole unit and the retry refuses properly.
        //
        // All three RosterEntries references carry the ambient query filter.
        var affected = await db.RosterEntries
            .Where(entry => entry.Id == rosterEntryId
                // Rule 3: the target must not itself be an alt.
                && !db.RosterEntries.Any(main => main.Id == mainRosterEntryId && main.MainRosterEntryId != null)
                // Rule 4: this entry must not already be somebody's main.
                && !db.RosterEntries.Any(alt => alt.MainRosterEntryId == rosterEntryId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.MainRosterEntryId, mainRosterEntryId),
                ct);

        return affected > 0;
    }

    public async Task UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct) =>
        await db.RosterEntries
            .Where(entry => entry.Id == rosterEntryId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.MainRosterEntryId, (Guid?)null),
                ct);

    private const string RosterIndexName = "IX_RosterEntries_TenantId_CharacterId";
    private const string AltForeignKeyName = "FK_RosterEntries_RosterEntries_MainRosterEntryId";

    // Two of this feature's rules are enforced by the database as well as by a check in Business,
    // deliberately: a check alone is a read-then-write and two racing requests both pass it. So both
    // constraints are translated here, on the transaction boundary where the SaveChanges that trips
    // them happens. Neither is retried — a constraint violation is not a transient failure.
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        try
        {
            return await db.ExecuteInTransactionAsync(operation, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolationOn(RosterIndexName))
        {
            // Two officers added the same character at once. Business pre-checks it; the unique index
            // decides the race.
            throw new CharacterAlreadyOnRosterException();
        }
        catch (DbUpdateException ex) when (ex.IsForeignKeyViolationOn(AltForeignKeyName))
        {
            // An alt was linked between the has-alts count and the delete. The count is deliberately
            // not re-queried: this path already lost the race, and the caller re-reads either way.
            throw new RosterEntryHasAltsException(altCount: null);
        }
    }
}
