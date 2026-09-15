using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Pass-throughs: each operation is already one repository call, nothing here reaches a gateway, and
// the only write is a single upsert. Kept as the seam Business depends on (add-endpoint skill), so the
// day naming a rank grows an audit row it becomes atomic here without Business changing.
public class GuildRankNameDataLayer(IGuildRankNameRepository repository) : IGuildRankNameDataLayer
{
    public Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct) =>
        repository.ListAsync(ct);

    public Task<bool> FollowsGuildAsync(Guid guildId, CancellationToken ct) =>
        repository.FollowsGuildAsync(guildId, ct);

    public Task SetAsync(Guid guildId, int rank, string? name, CancellationToken ct) =>
        repository.SetAsync(guildId, rank, name, ct);
}
