using Microsoft.AspNetCore.Identity;

namespace AegisScribe.ApiService.Managers.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? LastTenantId { get; set; }
}
