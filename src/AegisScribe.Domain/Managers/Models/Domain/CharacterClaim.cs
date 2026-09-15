namespace AegisScribe.Domain.Managers.Models.Domain;

// "That warrior is mine" (7.2b) — the first row in this repo tying an Identity user to a character.
//
// Tenant-scoped, and the "would two communities disagree" test is unusually clear here: the same
// player may be a known quantity in one community and a stranger in another, and a claim in one says
// nothing about the other. It also holds an FK INTO the global Character table, the allowed direction.
//
// UserId carries more weight than it looks. external.md makes a claim the PROOF OF OWNERSHIP that
// lets a signed-in user trigger a global Blizzard erasure for a character (14.1) — "a claim lives in
// one tenant, but the erasure it authorises is global". A claim that could be created naming somebody
// else would eventually become a way to delete their data, which is why every write path takes this
// value from ICurrentUser and there is no field on any ViewModel that could supply it.
//
// Claiming asserts nothing to Blizzard and grants no permission: rank and OfficerNote stay
// TenantOfficer-gated regardless of who claims what.
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
