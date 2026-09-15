namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class SlugCheckServiceModel
{
    // The canonical slug the check was performed against — derived from the name when the caller sent
    // one, echoed back when they sent a slug. Null when a name had nothing sluggable in it.
    public string? Slug { get; set; }

    // Available and Reason are both here on purpose, rather than letting the client infer one from the
    // other. Clients must tolerate an enum value they don't know (api-contract.md), and a client that
    // meets a Reason added after it shipped still has to know whether the slug is usable.
    public bool Available { get; set; }

    public SlugCheckReason Reason { get; set; }
}

public enum SlugCheckReason
{
    Available = 0,

    // A community already holds it. Deliberately says nothing about which one — this endpoint is
    // reachable by any authenticated caller and must not become a community-enumeration oracle.
    Taken = 1,

    Reserved = 2,

    // Only reachable via an explicit ?slug= — a derived slug is well-formed by construction.
    Invalid = 3,

    // The name folded to nothing usable, so there is no suggestion to offer and the caller must type
    // a slug themselves.
    NotDerivable = 4,
}
