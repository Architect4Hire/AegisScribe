using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class TenantMembershipServiceModel
{
    public Guid TenantId { get; set; }
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public TenantRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
