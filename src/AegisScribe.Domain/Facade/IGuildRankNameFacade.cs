using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface IGuildRankNameFacade
{
    Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct);

    Task<bool> SetAsync(Guid guildId, int rank, SetGuildRankNameViewModel viewModel, CancellationToken ct);
}
