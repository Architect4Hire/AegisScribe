using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface IGuildRankNameBusiness
{
    Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct);

    // False when this community does not follow that guild, which the controller turns into a 404.
    Task<bool> SetAsync(Guid guildId, int rank, SetGuildRankNameViewModel viewModel, CancellationToken ct);
}
