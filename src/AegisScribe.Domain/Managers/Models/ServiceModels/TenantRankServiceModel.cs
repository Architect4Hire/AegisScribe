namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// No TenantId on the wire. The caller already named the tenant in the route and could not have
// reached this response without a membership in it, so echoing the id back adds nothing a screen
// uses — and every response field is a promise for the life of v1 (api-contract.md).
public class TenantRankServiceModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Colour { get; set; } = string.Empty;
}
