namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class UserServiceModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public IReadOnlyList<TenantMembershipServiceModel> Memberships { get; set; } = [];
}
