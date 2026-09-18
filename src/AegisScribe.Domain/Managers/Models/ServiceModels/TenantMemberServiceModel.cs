using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// A person in this community, as their fellow members see them.
//
// DisplayName and no email: who is in your community is not privileged information inside it, but a
// fellow member's contact details are not theirs to hand out. Same line the roster projection draws.
public class TenantMemberServiceModel
{
    public string UserId { get; set; } = string.Empty;

    // Nullable because Identity's is: a user who never set one has no name to show, and the client
    // falls back rather than being handed an invented one.
    public string? DisplayName { get; set; }

    public TenantRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    // What this member has said is theirs, in THIS community. Empty rather than null when they have
    // claimed nothing: "nobody has claimed anything yet" is a real answer the member screen renders as
    // its own line, and a client branching on null vs [] would get it wrong half the time.
    //
    // It rides on the member projection rather than being a second call because the officer's question
    // is "who hasn't claimed a character", which is only answerable with both halves side by side. A
    // claim does NOT require the character to be on the roster, so deriving this from the roster would
    // under-report and turn that question into a lie.
    public IReadOnlyList<MemberClaimedCharacterServiceModel> ClaimedCharacters { get; set; } = [];
}

// One claimed character, as it appears beside the member who claimed it.
//
// Region + RealmSlug + Name are exactly the three segments the SPA's character route takes, so each of
// these renders as a link rather than dead text. Nothing here is about the user — the claim's own
// fields (who, when) are already implied by the member this hangs off.
public class MemberClaimedCharacterServiceModel
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public CharacterClass Class { get; set; }

    // Mapped API-side by CharacterMappers.ClassColorHex, for the same reason RosterEntryServiceModel
    // carries it: the frontend is forbidden a class→hex table of its own.
    public string ClassColor { get; set; } = string.Empty;
}
