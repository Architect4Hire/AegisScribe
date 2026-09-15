using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Mostly pass-throughs. Two of these are genuine compositions, and both exist so the claim vertical
// stays out of tables it does not own: CharacterExistsAsync reads the GLOBAL character table through
// its own repository, and ClearAsync pairs the claim removal with the audit row.
public class CharacterClaimDataLayer(
    ICharacterClaimRepository claims,
    ICharacterRepository characters,
    IAuditLogRepository auditLog) : ICharacterClaimDataLayer
{
    public Task<CharacterClaimServiceModel?> FindByCharacterAsync(Guid characterId, CancellationToken ct) =>
        claims.FindByCharacterAsync(characterId, ct);

    public Task<CharacterClaim?> FindEntityByCharacterAsync(Guid characterId, CancellationToken ct) =>
        claims.FindEntityByCharacterAsync(characterId, ct);

    public Task<bool> CharacterExistsAsync(Guid characterId, CancellationToken ct) =>
        characters.ExistsAsync(characterId, ct);

    public Task<CharacterClaim> AddAsync(CharacterClaim claim, CancellationToken ct) =>
        claims.AddAsync(claim, ct);

    public async Task RemoveAsync(CharacterClaim claim, CancellationToken ct) =>
        await claims.ExecuteInTransactionAsync(async token =>
        {
            await claims.RemoveAsync(claim, token);
            return true;
        }, ct);

    // The one write in this vertical that must be atomic: a removal that committed without its audit
    // row would be exactly the untraceable officer action the audit table exists to prevent.
    //
    // Both repositories resolve the same scoped DbContext, so both stage into one SaveChanges. Nothing
    // but repository calls goes in here — the callback is retryable.
    public async Task ClearAsync(CharacterClaim claim, AuditLog auditEntry, CancellationToken ct) =>
        await claims.ExecuteInTransactionAsync(async token =>
        {
            await claims.RemoveAsync(claim, token);
            await auditLog.AddAsync(auditEntry, token);
            return true;
        }, ct);
}
