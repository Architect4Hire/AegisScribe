namespace AegisScribe.Domain.Managers.Models.Domain;

public class Tenant
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string TimeZoneId { get; set; } = null!;
}
