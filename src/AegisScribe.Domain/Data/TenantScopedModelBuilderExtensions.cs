using System.Linq.Expressions;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public static class TenantScopedModelBuilderExtensions
{
    // Applied to every entity implementing ITenantScoped, so a developer adding entity #40 never has
    // to remember to wire up isolation by hand (tenancy.md). `ambientTenantId` is a small lambda
    // (`() => tenantContext.TenantId`) written directly in the caller's OnModelCreating, so its
    // compiled body naturally captures `this` (the DbContext instance) the same way EF Core's own
    // documented multi-tenant example does (learn.microsoft.com/ef/core/querying/filters) — that is
    // what lets each DbContext instance's own injected ITenantContext apply correctly at query time,
    // despite the compiled model being built, and cached, only once per context type.
    public static void ApplyTenantScopedQueryFilters(
        this ModelBuilder modelBuilder, Expression<Func<Guid>> ambientTenantId)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // Resolved by name against the CONCRETE entity type, not the interface — so EF Core sees
            // exactly the same member it mapped from reflection, with no interface/concrete ambiguity.
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantIdProperty = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
            var predicate = Expression.Equal(tenantIdProperty, ambientTenantId.Body);
            var filter = Expression.Lambda(predicate, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
