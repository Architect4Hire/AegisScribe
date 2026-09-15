namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// The claim state of one character, in one community. All three nullable fields are null together when
// nobody has claimed it.
//
// No `isMine` flag, and that is cache correctness rather than modelling preference: this response is
// cached under a TENANT key, so anything user-specific would be computed for the first caller and then
// served to every other member. ClaimedByUserId keeps the payload user-independent, and "is this mine"
// becomes a comparison against the user the SPA already has from GET /api/v1/me.
public class CharacterClaimServiceModel
{
    public Guid CharacterId { get; set; }

    public string? ClaimedByUserId { get; set; }

    // Null when the holder has set no display name — the UI shows "claimed" without a name rather than
    // falling back to an email address, which is not a member's to see.
    public string? ClaimedByDisplayName { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }
}
