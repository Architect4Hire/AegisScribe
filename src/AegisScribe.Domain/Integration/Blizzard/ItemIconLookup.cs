namespace AegisScribe.Domain.Integration.Blizzard;

// Blizzard's answer about one item's icon. A 404 is a normal answer — some items have no media
// (blizzard-endpoints.md → "Some icons 404") — so it is a value here rather than an exception, and the
// caller records it so the item is not asked about again every pass.
public sealed record ItemIconLookup(string? IconName)
{
    public static readonly ItemIconLookup NotFound = new((string?)null);
}
