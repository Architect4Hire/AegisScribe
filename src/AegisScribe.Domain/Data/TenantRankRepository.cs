using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class TenantRankRepository(AegisScribeDbContext db) : ITenantRankRepository
{
    public async Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct) =>
        await db.TenantRanks
            .AsNoTracking()
            .OrderBy(rank => rank.SortOrder)
            // The tie-break, because SortOrder is deliberately not unique: without it, two ranks at
            // the same position swap places between reads and the roster looks unstable.
            .ThenBy(rank => rank.Name)
            .Select(rank => new TenantRankServiceModel
            {
                Id = rank.Id,
                Name = rank.Name,
                SortOrder = rank.SortOrder,
                Colour = rank.Colour,
            })
            .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct) => db.TenantRanks.CountAsync(ct);

    // Tracked, not AsNoTracking: the caller is Business's update path, which mutates what comes back
    // and hands it to UpdateAsync.
    public Task<TenantRank?> FindAsync(Guid id, CancellationToken ct) =>
        db.TenantRanks.FirstOrDefaultAsync(rank => rank.Id == id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken ct) =>
        db.TenantRanks
            .AsNoTracking()
            // Case-insensitivity comes from the column's collation rather than a ToLower() call, which
            // keeps this sargable against IX_TenantRanks_TenantId_Name — and keeps the pre-check
            // answering the same question the unique index does, instead of a stricter one.
            .AnyAsync(rank => rank.Name == name && (excludingId == null || rank.Id != excludingId), ct);

    public Task AddAsync(TenantRank rank, CancellationToken ct)
    {
        db.TenantRanks.Add(rank);
        return SaveTranslatingNameCollisionAsync(rank.Name, ct);
    }

    public Task UpdateAsync(TenantRank rank, CancellationToken ct)
    {
        db.TenantRanks.Update(rank);
        return SaveTranslatingNameCollisionAsync(rank.Name, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        // Query-filtered, so this reaches only this community's rows: another tenant's rank id matches
        // nothing and deletes nothing, which is what makes the endpoint's unconditional 204 safe.
        await db.TenantRanks.Where(rank => rank.Id == id).ExecuteDeleteAsync(ct);
    }

    private const string NameIndexName = "IX_TenantRanks_TenantId_Name";

    // TenantRankBusiness pre-checks the name, but two officers can both pass that check before either
    // writes. The index is the authority; without this translation the loser of that race gets a 500
    // for a condition the client already knows how to handle. Same shape as TenantRepository's slug
    // translation, including matching on the index NAME — a different unique index breaking must not
    // be reported as a taken rank name.
    private async Task SaveTranslatingNameCollisionAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolationOn(NameIndexName))
        {
            throw new RankNameTakenException(name);
        }
    }

}
