namespace AegisScribe.Domain.Managers.Models.Domain;

// TenantId is never client-supplied (tenancy.md) and business code never assigns it — the SaveChanges
// interceptor is the only writer. So a tracked entity whose TenantId doesn't match the ambient tenant
// can only mean a bug upstream (an entity copied across tenant contexts, a cross-tenant background
// job forgetting to scope itself), not a normal request-time condition. The global exception handler
// maps it to 403 rather than letting it fall through as an unlabelled 500.
public class TenantStampMismatchException(string entityTypeName, Guid entityTenantId, Guid ambientTenantId)
    : Exception($"{entityTypeName} carries TenantId '{entityTenantId}', which does not match the " +
                $"ambient tenant '{ambientTenantId}'. TenantId must never be assigned by business code.");
