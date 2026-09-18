using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class JoinRequestServiceModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    // Nullable because Identity's is — see TenantMemberServiceModel.
    public string? DisplayName { get; set; }

    // The applicant's own words, written by somebody outside the community — untrusted input wherever
    // it lands, including near a prompt (ai.md).
    public string? Message { get; set; }

    public JoinRequestStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
