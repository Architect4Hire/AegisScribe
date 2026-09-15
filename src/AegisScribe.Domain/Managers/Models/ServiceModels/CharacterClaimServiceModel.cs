namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// The claim state of one character, in one community. All three nullable fields are null together
// when nobody has claimed it.
//
// There is deliberately no `isMine` flag, and the reason is a cache-correctness one rather than a
// modelling preference: this response is cached under a TENANT key (t:{tenantId}:claim:{characterId}),
// so anything user-specific in it would be computed for the first caller and then served to every
// other member of the community. A tenant-prefixed key would be correctly prefixed and still wrong.
//
// ClaimedByUserId keeps the payload user-independent, and "is this mine" becomes a comparison the
// client already has everything for — the SPA loads the signed-in user from GET /api/v1/me (auth.md),
// which is what the design reference's "Claimed by you" pill renders from.
public class CharacterClaimServiceModel
{
    public Guid CharacterId { get; set; }

    public string? ClaimedByUserId { get; set; }

    // Null when the holder has set no display name — the UI shows "claimed" without a name rather than
    // falling back to an email address, which is not a member's to see.
    public string? ClaimedByDisplayName { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }
}
