namespace AegisScribe.Domain.Managers.Models.Domain;

// Deliberately NOT ITenantScoped, unlike ordinary tenant-scoped data. TenantResolutionMiddleware
// checks membership BEFORE any tenant is resolved for the request — that IS how resolution happens —
// so an ambient filter reading ITenantContext.TenantId would throw on that exact lookup (TenantContext
// throws when unresolved). TenantMembership and Tenant are the two entities that bootstrap tenancy
// itself; every real query against this table (TenantRepository.IsMemberAsync included) already
// filters by an explicit TenantId parameter, so the ambient filter would only ever be redundant with
// that condition, not add safety beyond it. Do not "fix" this by adding ITenantScoped — see
// TenantScopedModelBuilderExtensions and TenantRepository for the lookups this would break.
public class TenantMembership
{
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = null!;
    public TenantRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
