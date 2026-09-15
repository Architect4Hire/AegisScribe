using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterDataLayer
{
    // The cache-first read (add-endpoint skill, step 6): local store first, Blizzard only when the row
    // is missing or has aged past the staleness policy, and the stale row rather than a failure when
    // Blizzard cannot answer.
    //
    // This sequencing is persistence bookkeeping, which is why it lives here and not in Business
    // (CLAUDE.md -> Usage). Business asks for a character; it does not know there is an upstream.
    Task<CharacterReadResult> GetCharacterAsync(string region, string realmSlug, string name, CancellationToken ct);

    // The same fetch-and-persist, with the staleness check skipped (6.6).
    //
    // Used by the officer-triggered refresh, where the caller has already paid for the calls out of
    // their community's budget and asked us to go and ask Blizzard. Serving a stored row here would
    // make the button a lie — and would have charged them for it.
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
