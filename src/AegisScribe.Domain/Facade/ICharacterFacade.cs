using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface ICharacterFacade
{
    Task<CharacterDetailServiceModel?> GetCharacterAsync(CharacterLookupViewModel viewModel, CancellationToken ct);

    Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchCharactersAsync(SearchCharactersViewModel viewModel, CancellationToken ct);
}
