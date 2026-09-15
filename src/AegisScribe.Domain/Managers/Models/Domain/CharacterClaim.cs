namespace AegisScribe.Domain.Managers.Models.Domain;

// "That warrior is mine" — ties an Identity user to a character, in one community. Tenant-scoped,
// because the same player may be a known quantity in one community and a stranger in another.
//
// UserId carries more weight than it looks: a claim is the PROOF OF OWNERSHIP that lets a signed-in
// user trigger a global Blizzard erasure (external.md). A claim created naming somebody else would be
// a way to delete their data, which is why every write path takes this from ICurrentUser and no
// ViewModel has a field that could supply it.
//
// Claiming asserts nothing to Blizzard and grants no permission.
public class CharacterClaim : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    // The Identity user id. Never client-supplied — see the note above.
    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}
