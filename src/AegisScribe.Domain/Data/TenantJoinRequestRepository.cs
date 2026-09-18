using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class TenantJoinRequestRepository(AegisScribeDbContext db) : ITenantJoinRequestRepository
{
    public async Task<IReadOnlyList<JoinRequestServiceModel>> ListAsync(
        Guid tenantId, JoinRequestStatus? status, int take, CancellationToken ct)
    {
        var requests = db.TenantJoinRequests.AsNoTracking().Where(r => r.TenantId == tenantId);

        if (status is not null)
        {
            requests = requests.Where(r => r.Status == status.Value);
        }

        return await requests
            .OrderByDescending(r => r.RequestedAt)
            .ThenBy(r => r.Id)
            .Take(take)
            .Join(db.Users, r => r.UserId, user => user.Id, (r, user) => new JoinRequestServiceModel
            {
                Id = r.Id,
                UserId = r.UserId,
                // DisplayName only, like every other person-shaped projection here.
                DisplayName = user.DisplayName,
                Message = r.Message,
                Status = r.Status,
                RequestedAt = r.RequestedAt,
                DecidedAt = r.DecidedAt,
            })
            .ToListAsync(ct);
    }

    public Task<TenantJoinRequest?> FindAsync(Guid tenantId, Guid requestId, CancellationToken ct) =>
        db.TenantJoinRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == requestId, ct);

    public Task<bool> HasPendingAsync(Guid tenantId, string userId, CancellationToken ct) =>
        db.TenantJoinRequests
            .AsNoTracking()
            .AnyAsync(
                r => r.TenantId == tenantId && r.UserId == userId && r.Status == JoinRequestStatus.Pending,
                ct);

    public async Task<bool> TryDecideAsync(
        Guid tenantId,
        Guid requestId,
        JoinRequestStatus status,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var affected = await db.TenantJoinRequests
            .Where(r => r.TenantId == tenantId
                && r.Id == requestId
                && r.Status == JoinRequestStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.DecidedByUserId, decidedByUserId)
                    .SetProperty(r => r.DecidedAt, decidedAt),
                ct);

        return affected > 0;
    }

    public async Task<bool> TryResolvePendingForUserAsync(
        Guid tenantId,
        string userId,
        JoinRequestStatus status,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var affected = await db.TenantJoinRequests
            .Where(r => r.TenantId == tenantId
                && r.UserId == userId
                && r.Status == JoinRequestStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.DecidedByUserId, decidedByUserId)
                    .SetProperty(r => r.DecidedAt, decidedAt),
                ct);

        return affected > 0;
    }

    public Task AddAsync(TenantJoinRequest request, CancellationToken ct)
    {
        db.TenantJoinRequests.Add(request);

        return Task.CompletedTask;
    }

    private const string PendingIndexName = "IX_TenantJoinRequests_TenantId_UserId";

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        try
        {
            return await db.ExecuteInTransactionAsync(operation, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolationOn(PendingIndexName))
        {
            // The filtered unique index decides the race that Business only pre-checks: somebody
            // double-submitting the join form has two requests in flight, and exactly one may land.
            throw new JoinRequestAlreadyPendingException();
        }
    }
}
