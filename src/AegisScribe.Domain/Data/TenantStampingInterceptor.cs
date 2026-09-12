using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AegisScribe.Domain.Data;

// tenancy.md: TenantId is stamped by SaveChanges, never by business code. Added entities with no
// TenantId set get the ambient tenant; Added or Modified entities that already carry a DIFFERENT
// tenant's id throw — that's not a stamping gap, it's a cross-tenant write in progress. Reading
// tenantContext.TenantId is deferred until an ITenantScoped entry is actually in the changeset, so a
// SaveChanges on tenant-less data still works with no tenant resolved for the request.
public sealed class TenantStampingInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampAndValidate(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StampAndValidate(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAndValidate(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantContext.TenantId;
                continue;
            }

            if (entry.Entity.TenantId != tenantContext.TenantId)
            {
                throw new TenantStampMismatchException(
                    entry.Entity.GetType().Name, entry.Entity.TenantId, tenantContext.TenantId);
            }
        }
    }
}
