using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterClaimDataLayer
{
    Task<CharacterClaimServiceModel?> FindByCharacterAsync(Guid characterId, CancellationToken ct);

    Task<CharacterClaim?> FindEntityByCharacterAsync(Guid characterId, CancellationToken ct);

    // Whether the global Character exists at all. Composed from the character repository, so the claim
    // vertical never queries a table it does not own.
    Task<bool> CharacterExistsAsync(Guid characterId, CancellationToken ct);

    Task<CharacterClaim> AddAsync(CharacterClaim claim, CancellationToken ct);

    // A member releasing their own claim: one write, no audit row. Who may do this is Business's rule.
    Task RemoveAsync(CharacterClaim claim, CancellationToken ct);

    // An officer freeing somebody else's: removal and audit row together, or neither.
    Task ClearAsync(CharacterClaim claim, AuditLog auditEntry, CancellationToken ct);
}
