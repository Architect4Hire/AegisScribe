using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// The tenant-scoped half of guild linking. Separate from IGuildRepository because they are in
// different zones (tenancy.md), and a repository that wrote both would be the place a TenantId
// eventually leaked onto a global row.
public interface ITenantGuildRepository
{
    // Query-filtered, so this is already "this tenant's" links and cannot return another community's.
    Task<IReadOnlyList<TenantGuild>> ListAsync(CancellationToken ct);

    Task<TenantGuild?> FindAsync(Guid guildId, CancellationToken ct);

    // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
    Task LinkAsync(Guid guildId, DateTimeOffset linkedAt, CancellationToken ct);

    Task UnlinkAsync(Guid guildId, CancellationToken ct);
}
