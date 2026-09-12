using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

// No rules yet: a character page is public reference data (tenancy.md), so there's no "may this
// caller see it" check to apply here, unlike a claim or an edit.
public class CharacterBusiness(ICharacterDataLayer dataLayer) : ICharacterBusiness
{
    public async Task<CharacterDetailServiceModel?> GetCharacterAsync(string region, string realmSlug, string name, CancellationToken ct)
    {
        var character = await dataLayer.FindByRealmAndNameAsync(region, realmSlug, name, ct);
        return character?.ToServiceModel();
    }

    // A list read passes the data layer's projected summaries straight through (add-endpoint skill) —
    // they're already the outbound shape, so there's nothing left to translate.
    public Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchCharactersAsync(
        string region, string? realmSlug, string? nameContains, string? afterNameLower, Guid? afterId, int take, CancellationToken ct) =>
        dataLayer.SearchAsync(region, realmSlug, nameContains, afterNameLower, afterId, take, ct);
}
