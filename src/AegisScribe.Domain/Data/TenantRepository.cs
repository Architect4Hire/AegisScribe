using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

// These run BEFORE any tenant is resolved — they are how the tenant gets resolved. When 2.4 adds the
// convention query filter, TenantMembership's lookup here needs a deliberate answer; it must not
// become an IgnoreQueryFilters() on the request path (tenancy.md).
public class TenantRepository(AegisScribeDbContext db) : ITenantRepository
{
    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct) =>
        db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct) =>
        db.TenantMemberships
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);

    public Task<TenantRole?> GetRoleAsync(Guid tenantId, string userId, CancellationToken ct) =>
        db.TenantMemberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => (TenantRole?)m.Role)
            .FirstOrDefaultAsync(ct);

    // A list read projects straight to the outbound ServiceModel (add-endpoint skill) — a person's own
    // membership count is small and unpaginated, unlike a roster.
    public async Task<IReadOnlyList<TenantMembershipServiceModel>> GetMembershipsForUserAsync(string userId, CancellationToken ct) =>
        await db.TenantMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new TenantMembershipServiceModel
            {
                TenantId = t.Id,
                TenantSlug = t.Slug,
                TenantName = t.Name,
                Role = m.Role,
                JoinedAt = m.JoinedAt,
            })
            .ToListAsync(ct);

    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task AddAsync(Tenant tenant, CancellationToken ct)
    {
        db.Tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public Task AddMembershipAsync(TenantMembership membership, CancellationToken ct)
    {
        db.TenantMemberships.Add(membership);
        return Task.CompletedTask;
    }

    public Task UpdateNameAsync(Tenant tenant, CancellationToken ct)
    {
        db.Tenants.Update(tenant);
        return db.SaveChangesAsync(ct);
    }

    // The Aspire-enabled execution strategy refuses to run inside a caller-opened transaction (backend.md),
    // so the whole unit is handed in as a callback rather than exposing BeginTransactionAsync. The
    // callback may run more than once on a transient failure — safe here because AddAsync/AddMembershipAsync
    // only mark already-constructed entities as tracked, which is a no-op to repeat for the same instance.
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
