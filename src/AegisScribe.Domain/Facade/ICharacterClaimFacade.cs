using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface ICharacterClaimFacade
{
    Task<CharacterClaimServiceModel> GetAsync(Guid characterId, CancellationToken ct);

    Task<CharacterClaimServiceModel?> ClaimAsync(ClaimCharacterViewModel viewModel, CancellationToken ct);

    Task ReleaseAsync(Guid characterId, CancellationToken ct);

    Task ClearAsync(Guid characterId, CancellationToken ct);
}
