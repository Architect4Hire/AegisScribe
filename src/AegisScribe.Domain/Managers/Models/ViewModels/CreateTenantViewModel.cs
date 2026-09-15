namespace AegisScribe.Domain.Managers.Models.ViewModels;

public class CreateTenantViewModel
{
    // Optional since 2.7b: omitted, Business derives it from Name (TenantSlugRules.Derive) so no client
    // has to carry a copy of the derivation rules.
    public string? Slug { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
}
