namespace AegisScribe.Domain.Managers.Models.Domain;

public enum ItemQuality
{
    Poor,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Artifact,

    // Appended rather than slotted into rarity order: the value crosses the wire as a string
    // (api-contract.md) but is persisted as an int, so renumbering the members above would silently
    // re-colour every stored item. Blizzard sends HEIRLOOM on any levelling alt's gear, and the design
    // system has always had a --q-heirloom token waiting for it.
    Heirloom,
}
