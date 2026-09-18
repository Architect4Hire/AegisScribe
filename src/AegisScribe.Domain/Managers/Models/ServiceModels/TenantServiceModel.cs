namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class TenantServiceModel
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;

    // Owner-visible on this Owner-only route. The setting is meaningless to a member, and the screen
    // that toggles it is the one that reads it.
    public bool AcceptsJoinRequests { get; set; }
}
