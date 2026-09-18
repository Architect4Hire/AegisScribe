using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ViewModels;

// An officer mints a single-use link for one role in this community.
//
// No tenant id: the community is the resolved one, from the route (tenancy.md). No invitee identity
// either — we do not send these (email is out of bounds), so an officer copies the link and passes it
// on themselves. Note is how they tell two outstanding links apart.
public class CreateInvitationViewModel
{
    public TenantRole Role { get; set; }

    public string? Note { get; set; }

    // Optional; Business defaults it. A ceiling rather than a policy knob — a link that lives forever
    // is a standing key to the community.
    public int? ExpiresInDays { get; set; }
}
