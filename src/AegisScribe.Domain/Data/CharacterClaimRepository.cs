using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class CharacterClaimRepository(AegisScribeDbContext db) : ICharacterClaimRepository
{
    public Task<CharacterClaimServiceModel?> FindByCharacterAsync(Guid characterId, CancellationToken ct) =>
        db.CharacterClaims
            .AsNoTracking()
            .Where(claim => claim.CharacterId == characterId)
            // Joined rather than navigated because ApplicationUser is Identity's table, not a
            // navigation on CharacterClaim — and the display name is the only column of it this
            // response is allowed to carry. An email address is not a fellow member's to see.
            .Join(
                db.Users,
                claim => claim.UserId,
                user => user.Id,
                (claim, user) => new CharacterClaimServiceModel
                {
                    CharacterId = claim.CharacterId,
                    ClaimedByUserId = claim.UserId,
                    ClaimedByDisplayName = user.DisplayName,
                    ClaimedAt = claim.ClaimedAt,
                })
            .FirstOrDefaultAsync(ct);

    // Tracked: the callers mutate or remove what comes back.
    public Task<CharacterClaim?> FindEntityByCharacterAsync(Guid characterId, CancellationToken ct) =>
        db.CharacterClaims.FirstOrDefaultAsync(claim => claim.CharacterId == characterId, ct);

    public async Task<IReadOnlyList<Guid>> ListCharacterIdsClaimedByAsync(string userId, CancellationToken ct) =>
        await db.CharacterClaims
            .AsNoTracking()
            .Where(claim => claim.UserId == userId)
            .Select(claim => claim.CharacterId)
            .ToListAsync(ct);

    public async Task RemoveClaimsByUserAsync(string userId, CancellationToken ct) =>
        await db.CharacterClaims
            .Where(claim => claim.UserId == userId)
            .ExecuteDeleteAsync(ct);

    public async Task<CharacterClaim> AddAsync(CharacterClaim claim, CancellationToken ct)
    {
        db.CharacterClaims.Add(claim);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolationOn(ClaimIndexName))
        {
            // The race the Business pre-check cannot close: two members both find a character
            // unclaimed, and the unique index decides. The loser gets the same 409 the pre-check
            // produces rather than a 500 — but with no display name, because resolving who won would
            // need another round-trip for an error the client will re-read the claim after anyway.
            throw new CharacterAlreadyClaimedException(claimedByDisplayName: null);
        }

        return claim;
    }

    // Stages only. The officer's clear pairs this with an audit-row insert in one transaction, so
    // SaveChanges belongs to the unit — and deliberately NOT ExecuteDeleteAsync, which would run
    // immediately and outside it, leaving the claim gone with no audit row if the insert then failed.
    public Task RemoveAsync(CharacterClaim claim, CancellationToken ct)
    {
        db.CharacterClaims.Remove(claim);

        return Task.CompletedTask;
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(operation, ct);

    private const string ClaimIndexName = "IX_CharacterClaims_TenantId_CharacterId";
}
