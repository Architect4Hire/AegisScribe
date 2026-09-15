using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface IGuildRankNameDataLayer
{
    Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct);

    Task<bool> FollowsGuildAsync(Guid guildId, CancellationToken ct);

    Task SetAsync(Guid guildId, int rank, string? name, CancellationToken ct);
}
