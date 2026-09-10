using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AegisScribe.ApiService.Auth;

public static class RoleSeeder
{
    // Only PlatformAdmin. "Member"/"Officer"/"Owner" are tenant membership, not Identity roles — see auth.md.
    public static async Task SeedPlatformAdminRoleAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(AuthPolicies.PlatformAdmin))
        {
            await roleManager.CreateAsync(new IdentityRole(AuthPolicies.PlatformAdmin));
        }
    }
}
