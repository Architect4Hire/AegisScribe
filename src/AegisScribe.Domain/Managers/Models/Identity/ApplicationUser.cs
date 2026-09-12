using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Managers.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? LastTenantId { get; set; }
}
