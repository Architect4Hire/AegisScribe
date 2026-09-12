namespace AegisScribe.Domain.Context;

public interface ITenantContext
{
    Guid TenantId { get; }
}
