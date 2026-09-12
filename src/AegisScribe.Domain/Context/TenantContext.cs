namespace AegisScribe.Domain.Context;

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException(
        "No tenant has been resolved for this request. ITenantContext.TenantId was read outside " +
        "tenant scope — either this code ran on a tenant-less route, or it ran before tenant " +
        "resolution middleware (added in 2.3). A tenant-scoped query filter or facade must never " +
        "catch this exception and substitute a default.");

    public void SetTenant(Guid tenantId) => _tenantId = tenantId;
}
