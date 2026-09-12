namespace AegisScribe.ApiService.Auth;

public static class AuthPolicies
{
    public const string PlatformAdmin = "PlatformAdmin";

    // Ordered ranks (tenancy.md): Owner implies Officer implies Member. One TenantRoleRequirement
    // instance per constant, compared via TenantRole's own ordering — see TenantRoleAuthorizationHandler.
    public const string TenantMember = "TenantMember";
    public const string TenantOfficer = "TenantOfficer";
    public const string TenantOwner = "TenantOwner";
}
