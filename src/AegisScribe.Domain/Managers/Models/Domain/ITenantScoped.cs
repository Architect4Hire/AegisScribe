namespace AegisScribe.Domain.Managers.Models.Domain;

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
