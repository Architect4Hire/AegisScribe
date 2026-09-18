using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The target is the userId in the route; this carries only what they become. Whether the actor may
// grant it, and whether the target is theirs to touch, are Business's — both need the actor's own role.
public class SetMemberRoleViewModel
{
    public TenantRole Role { get; set; }
}
