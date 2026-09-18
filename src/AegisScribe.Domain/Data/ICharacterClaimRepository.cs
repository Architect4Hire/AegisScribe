using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Every method runs under CharacterClaim's global query filter, so there is no tenantId parameter here
// and a claim held in another community simply does not resolve (tenancy.md).
public interface ICharacterClaimRepository
{
    // The read the endpoint returns, projected in SQL with the holder's display name joined in. Null
    // when nobody in THIS community has claimed the character — which is also what a claim held in
    // another community looks like from here, correctly.
    Task<CharacterClaimServiceModel?> FindByCharacterAsync(Guid characterId, CancellationToken ct);

    // The domain entity, for the paths that have to decide something about the holder — releasing your
    // own claim, or an officer freeing someone else's.
    Task<CharacterClaim?> FindEntityByCharacterAsync(Guid characterId, CancellationToken ct);

    // The characters this person has claimed in THIS community — the input to removing them from it.
    // Query-filtered, so a claim they hold next door is neither listed nor removed.
    Task<IReadOnlyList<Guid>> ListCharacterIdsClaimedByAsync(string userId, CancellationToken ct);

    // Stages the removal of every claim this person holds here. Part of removing a member: a claim is
    // tenant-scoped and means nothing without the membership behind it.
    Task RemoveClaimsByUserAsync(string userId, CancellationToken ct);

    // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
    Task<CharacterClaim> AddAsync(CharacterClaim claim, CancellationToken ct);

    // Stages the removal only — the officer's clear composes this with an audit-row insert inside one
    // transaction, so the SaveChanges belongs to the unit rather than to this call.
    Task RemoveAsync(CharacterClaim claim, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
