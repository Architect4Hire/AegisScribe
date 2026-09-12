namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class TenantServiceModel
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
}
