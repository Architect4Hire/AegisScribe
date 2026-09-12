namespace AegisScribe.Domain.Managers.Models.ViewModels;

public class CreateTenantViewModel
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
}
