using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterDataLayer
{
    Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct);

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
