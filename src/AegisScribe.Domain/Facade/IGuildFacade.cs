using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface IGuildFacade
{
    Task<IReadOnlyList<GuildServiceModel>> ListAsync(CancellationToken ct);

    Task<GuildServiceModel?> LinkAsync(Guid tenantId, LinkGuildViewModel viewModel, CancellationToken ct);

    Task<GuildServiceModel?> ResyncAsync(Guid tenantId, Guid guildId, CancellationToken ct);

    Task<bool> UnlinkAsync(Guid guildId, CancellationToken ct);
}
