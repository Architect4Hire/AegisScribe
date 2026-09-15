using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface ICharacterClaimBusiness
{
    // Null when nobody in this community has claimed the character.
    Task<CharacterClaimServiceModel?> GetAsync(Guid characterId, CancellationToken ct);

    // Claims for the CALLER. Null when no such character exists, which the controller turns into a
    // 404; throws CharacterAlreadyClaimedException when another member holds it.
    Task<CharacterClaimServiceModel?> ClaimAsync(ClaimCharacterViewModel viewModel, CancellationToken ct);

    // Releases the caller's own claim. Throws ClaimNotYoursException when it is someone else's; a
    // character with no claim is a satisfied intent, not an error.
    Task ReleaseAsync(Guid characterId, CancellationToken ct);

    // An officer frees whoever holds it, writing an audit row. Never reassigns.
    Task ClearAsync(Guid characterId, CancellationToken ct);
}
