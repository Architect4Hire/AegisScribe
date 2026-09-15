using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterDataLayer
{
    // The cache-first read: local store first, Blizzard only when the row is missing or stale, and the
    // stale row rather than a failure when Blizzard cannot answer. Persistence bookkeeping, which is
    // why it lives here rather than in Business — Business does not know there is an upstream.
    Task<CharacterReadResult> GetCharacterAsync(string region, string realmSlug, string name, CancellationToken ct);

    // The same fetch-and-persist with the staleness check skipped, for the officer-triggered refresh.
    // The caller has already paid for the calls out of their community's budget, so serving a stored
    // row would make the button a lie and charge them for it.
    Task<CharacterReadResult> RefreshCharacterAsync(string region, string realmSlug, string name, CancellationToken ct);

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
