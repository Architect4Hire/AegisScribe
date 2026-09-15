using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

// The one-claim-per-character rule and the "this claim belongs to another user" resource check both
// live here, because both need to read something before they can answer. Policies answer "what rank are
// you here"; Business answers "is this yours" (auth.md).
//
// Every write takes the claimant from ICurrentUser and never from a view model. That is load-bearing
// beyond ordinary permissions: a claim is the proof of ownership behind a user-triggered global
// erasure, so a claim naming somebody else would become a way to delete their data.
public class CharacterClaimBusiness(
    ICharacterClaimDataLayer dataLayer,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICharacterClaimBusiness
{
    public Task<CharacterClaimServiceModel?> GetAsync(Guid characterId, CancellationToken ct) =>
        dataLayer.FindByCharacterAsync(characterId, ct);

    public async Task<CharacterClaimServiceModel?> ClaimAsync(
        ClaimCharacterViewModel viewModel, CancellationToken ct)
    {
        var userId = RequireUserId();

        // Without this the foreign key would turn an unknown character into a 500. The controller turns
        // null into the 404 it should have been.
        if (!await dataLayer.CharacterExistsAsync(viewModel.CharacterId, ct))
        {
            return null;
        }

        // Read before inserting: deleting this check gives a character two owners, which makes it a rule
        // rather than bookkeeping.
        var existing = await dataLayer.FindByCharacterAsync(viewModel.CharacterId, ct);

        if (existing is not null)
        {
            // Already the caller's: a satisfied intent, not a conflict. A mobile client whose response
            // was dropped retries, and that retry must not surface as a 409.
            if (existing.ClaimedByUserId == userId)
            {
                return existing;
            }

            throw new CharacterAlreadyClaimedException(existing.ClaimedByDisplayName);
        }

        var claim = new CharacterClaim
        {
            Id = Guid.NewGuid(),
            CharacterId = viewModel.CharacterId,
            // From the caller, never the request. TenantId is absent on purpose — the interceptor
            // stamps it (tenancy.md).
            UserId = userId,
            ClaimedAt = timeProvider.GetUtcNow(),
        };

        await dataLayer.AddAsync(claim, ct);

        // Re-read rather than mapping the entity: the response carries the holder's display name, which
        // lives on the Identity user and not on the row just written.
        return await dataLayer.FindByCharacterAsync(viewModel.CharacterId, ct);
    }

    public async Task ReleaseAsync(Guid characterId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var claim = await dataLayer.FindEntityByCharacterAsync(characterId, ct);

        // Nothing to release is a satisfied intent — DELETE is idempotent (api-contract.md). This also
        // covers a claim held in another community, which the query filter correctly makes
        // indistinguishable from absent.
        if (claim is null)
        {
            return;
        }

        // An officer wanting this outcome has their own route, which frees the claim and records that
        // they did.
        if (claim.UserId != userId)
        {
            throw new ClaimNotYoursException();
        }

        await dataLayer.RemoveAsync(claim, ct);
    }

    public async Task ClearAsync(Guid characterId, CancellationToken ct)
    {
        var actorUserId = RequireUserId();
        var claim = await dataLayer.FindEntityByCharacterAsync(characterId, ct);

        // No claim, no action, and so no audit row: an audit table recording things that did not happen
        // would be worse than one with gaps.
        if (claim is null)
        {
            return;
        }

        var auditEntry = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            // Who was acted upon — the whole reason the row exists, since this is an officer reaching
            // into another member's data.
            SubjectUserId = claim.UserId,
            Action = AuditAction.CharacterClaimCleared,
            TargetType = nameof(CharacterClaim),
            TargetId = claim.CharacterId,
            Before = $"Claimed by {claim.UserId}",
            // Freed, not reassigned — an officer's clear never hands a character to someone else.
            After = "Unclaimed",
            OccurredAt = timeProvider.GetUtcNow(),
        };

        await dataLayer.ClearAsync(claim, auditEntry, ct);
    }

    // Every path here is behind an authenticated tenant policy, so a null user id means a
    // client-credentials token, which names no Identity user. Failing loudly beats writing a claim or an
    // audit row with an empty owner.
    private string RequireUserId() =>
        currentUser.UserId ?? throw new AuthenticationRequiredException();
}
