using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Pass-throughs today, and correct as such (4.2, add-endpoint skill): each operation is one
// repository call. The seam is what lets Phase 6 add cache-first Blizzard reads here without
// touching Business.
public class CharacterDataLayer(ICharacterRepository repository) : ICharacterDataLayer
{
    public Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct) =>
        repository.FindByRealmAndNameAsync(region, realmSlug, name, ct);

    public Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region, string? realmSlug, string? nameContains, string? afterNameLower, Guid? afterId, int take, CancellationToken ct) =>
        repository.SearchAsync(region, realmSlug, nameContains, afterNameLower, afterId, take, ct);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(operation, ct);
}
