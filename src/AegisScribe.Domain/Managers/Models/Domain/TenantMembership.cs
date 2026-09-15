namespace AegisScribe.Domain.Managers.Models.Domain;

// Deliberately NOT ITenantScoped. TenantResolutionMiddleware checks membership BEFORE any tenant is
// resolved — that IS how resolution happens — so an ambient filter reading ITenantContext.TenantId
// would throw on that exact lookup. Every real query here already filters by an explicit TenantId
// parameter, so the filter would be redundant rather than safer. Do not "fix" this by adding
// ITenantScoped.
public class TenantMembership
{
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = null!;
    public TenantRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
