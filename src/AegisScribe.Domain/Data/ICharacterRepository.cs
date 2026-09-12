using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterRepository
{
    // Detail read — the domain entity, gear included, for a character page.
    Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct);

    // List read — projects straight to the summary ServiceModel in SQL. Cursor pagination
    // (api-contract.md) is seek-based: afterNameLower/afterId are the decoded tie-break from the
    // previous page, not the opaque cursor string — that encoding stays above this layer.
    Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region,
        string? realmSlug,
        string? nameContains,
        string? afterNameLower,
        Guid? afterId,
        int take,
        CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
