using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// Not cached. The linked-guild list is small, the roster behind it changes whenever somebody gquits,
// and a cached member count that disagrees with the roster screen is worse than the read it saved.
public class GuildFacade(
    IGuildBusiness business,
    IValidator<LinkGuildViewModel> linkValidator) : IGuildFacade
{
    public Task<IReadOnlyList<GuildServiceModel>> ListAsync(CancellationToken ct) => business.ListAsync(ct);

    public async Task<GuildServiceModel?> LinkAsync(Guid tenantId, LinkGuildViewModel viewModel, CancellationToken ct)
    {
        // Validated before Business charges the budget — a malformed realm slug should not cost a
        // community a Blizzard call it could have spent on a real guild.
        await linkValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.LinkAsync(tenantId, viewModel.Region, viewModel.RealmSlug, viewModel.GuildName, ct);
    }

    public Task<GuildServiceModel?> ResyncAsync(Guid tenantId, Guid guildId, CancellationToken ct) =>
        business.ResyncAsync(tenantId, guildId, ct);

    public Task<bool> UnlinkAsync(Guid guildId, CancellationToken ct) => business.UnlinkAsync(guildId, ct);
}
