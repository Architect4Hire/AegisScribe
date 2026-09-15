using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Query-filtered like every tenant-scoped repository, so there is no tenantId parameter and another
// community's names for the same global guild simply do not resolve (tenancy.md).
public interface IGuildRankNameRepository
{
    /// <summary>
    /// Every rank 0-9 of every guild this community follows, named where somebody has named it.
    /// </summary>
    /// <remarks>
    /// Returns the full ten rows per guild rather than only the stored ones, because the editor is a
    /// form over a fixed range and an absent row is a meaningful, editable state rather than a gap.
    /// </remarks>
    Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct);

    // The guard on the write: without it an officer could name the ranks of a guild they have nothing
    // to do with, and the row would then be invisible because nothing joins to it.
    Task<bool> FollowsGuildAsync(Guid guildId, CancellationToken ct);

    // Upsert on (TenantId, GuildId, Rank). A null name removes the row rather than storing an empty
    // string — absent and "" would otherwise render differently for no reason a user could explain.
    Task SetAsync(Guid guildId, int rank, string? name, CancellationToken ct);
}
