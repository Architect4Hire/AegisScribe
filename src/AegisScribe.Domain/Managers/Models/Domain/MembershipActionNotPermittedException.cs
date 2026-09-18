namespace AegisScribe.Domain.Managers.Models.Domain;

// An actor reached for somebody at or above their own rank, or tried to grant a role above it.
//
// A domain rule rather than a policy, because it compares the ACTOR's role to the TARGET's and both
// come from data (auth.md: "policies answer what rank are you here; Business answers is this yours").
// The route's TenantOfficer policy has already passed by the time this throws.
public class MembershipActionNotPermittedException(string reason) : Exception(reason)
{
    public static MembershipActionNotPermittedException OutranksActor() =>
        new("You may only act on members below your own rank.");

    public static MembershipActionNotPermittedException RoleAboveActor() =>
        new("You may not grant a role above your own.");
}
