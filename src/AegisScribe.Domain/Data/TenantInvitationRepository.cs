using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class TenantInvitationRepository(AegisScribeDbContext db) : ITenantInvitationRepository
{
    public async Task<IReadOnlyList<TenantInvitation>> ListAsync(Guid tenantId, int take, CancellationToken ct) =>
        await db.TenantInvitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            // Outstanding first — an officer opens this screen to find the link they have not sent
            // yet. Expiry is not in the ordering because it is a clock comparison, not a column, and
            // an expired row is still "not accepted, not revoked".
            .OrderBy(i => i.AcceptedAt == null && i.RevokedAt == null ? 0 : 1)
            .ThenByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Take(take)
            .ToListAsync(ct);

    public Task<TenantInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.TenantInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public async Task<bool> TryConsumeAsync(
        byte[] tokenHash, string acceptedByUserId, DateTimeOffset acceptedAt, CancellationToken ct)
    {
        // Live is the condition, and all three halves of it are in the statement: not yet accepted,
        // not revoked, not expired. Whoever's UPDATE takes the row lock first wins the invitation; the
        // loser sees zero rows affected and re-reads to find out which of the three now holds.
        var affected = await db.TenantInvitations
            .Where(i => i.TokenHash == tokenHash
                && i.AcceptedAt == null
                && i.RevokedAt == null
                && i.ExpiresAt > acceptedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.AcceptedAt, acceptedAt)
                    .SetProperty(i => i.AcceptedByUserId, acceptedByUserId),
                ct);

        return affected > 0;
    }

    public Task<TenantInvitation?> FindAsync(Guid tenantId, Guid invitationId, CancellationToken ct) =>
        db.TenantInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invitationId, ct);

    public async Task<bool> TryRevokeAsync(
        Guid tenantId, Guid invitationId, string revokedByUserId, DateTimeOffset revokedAt, CancellationToken ct)
    {
        // Still outstanding is the condition, and it is in the statement rather than in front of it.
        // An expired invitation is deliberately still revocable: the officer's intent is "this link is
        // dead", and refusing because the clock got there first would be pedantry.
        var affected = await db.TenantInvitations
            .Where(i => i.TenantId == tenantId
                && i.Id == invitationId
                && i.AcceptedAt == null
                && i.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.RevokedAt, revokedAt)
                    .SetProperty(i => i.RevokedByUserId, revokedByUserId),
                ct);

        return affected > 0;
    }

    public Task AddAsync(TenantInvitation invitation, CancellationToken ct)
    {
        db.TenantInvitations.Add(invitation);

        return Task.CompletedTask;
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(operation, ct);
}
