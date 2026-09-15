namespace AegisScribe.Domain.Managers.Models.Domain;

// The alt model is one level deep: a main has no main. Refusing both ways to violate it is what makes
// cycles impossible rather than merely unlikely — every cycle needs a node that is both a main and an
// alt. One type with two reasons, because a client branches on "this is a depth problem" and renders
// the remedy, which differs by reason.
public class AltDepthException(AltDepthReason reason) : Exception(Describe(reason))
{
    public AltDepthReason Reason { get; } = reason;

    private static string Describe(AltDepthReason reason) => reason switch
    {
        AltDepthReason.TargetIsAlreadyAnAlt =>
            "That character is already somebody's alt. Link to their main instead.",
        AltDepthReason.EntryAlreadyHasAlts =>
            "That character has alts of its own. Detach them before making it an alt.",
        _ => "That link would nest alts more than one level deep.",
    };
}

public enum AltDepthReason
{
    // Naming an entry that is itself an alt — depth 2 from below.
    TargetIsAlreadyAnAlt,

    // Making a main into an alt while other entries still point at it — depth 2 from above.
    EntryAlreadyHasAlts,
}
