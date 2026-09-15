using System.Linq.Expressions;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public static class TenantScopedModelBuilderExtensions
{
    // Applied to every ITenantScoped entity, so adding entity #40 never means remembering to wire up
    // isolation by hand (tenancy.md).
    //
    // `ambientTenantId` is a lambda written in the caller's OnModelCreating, so its compiled body
    // captures the DbContext instance — which is what lets each instance's own injected ITenantContext
    // apply at query time, despite the compiled model being cached once per context type.
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
