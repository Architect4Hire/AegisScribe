using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public interface ICharacterBusiness
{
    Task<CharacterDetailServiceModel?> GetCharacterAsync(string region, string realmSlug, string name, CancellationToken ct);

    Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchCharactersAsync(
        string region,
        string? realmSlug,
        string? nameContains,
        string? afterNameLower,
        Guid? afterId,
        int take,
        CancellationToken ct);
}
