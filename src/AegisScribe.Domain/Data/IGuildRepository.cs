using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Guild and GuildMember are GLOBAL reference data (tenancy.md) — the same guild, with the same roster
// and the same in-game ranks, whoever is looking. Which communities follow it is TenantGuild's problem,
// and lives in the other zone.
public interface IGuildRepository
{
    Task<Guild?> FindAsync(Guid realmId, string guildName, CancellationToken ct);

    Task<Guild?> FindByIdAsync(Guid guildId, CancellationToken ct);

    // Guilds past the refresh window, for the worker. Global, so it needs no tenant and must not
    // iterate them: a Guild row exists only because somebody linked it, which is what lets the worker
    // refresh "every linked guild" without ever reading tenant data.
    Task<IReadOnlyList<Guild>> FindStaleAsync(DateTimeOffset staleBefore, int take, CancellationToken ct);

    Task<Guild> UpsertAsync(Guild fresh, Guid realmId, CancellationToken ct);

    // Reconciles the whole membership in one operation: joins inserted, leavers removed, rank changes
    // updated. A leaver MUST disappear — a roster that only ever grows would show people who quit.
    Task ReplaceMembersAsync(Guid guildId, IReadOnlyDictionary<Guid, int> ranksByCharacterId, CancellationToken ct);

    Task<int> CountMembersAsync(Guid guildId, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
