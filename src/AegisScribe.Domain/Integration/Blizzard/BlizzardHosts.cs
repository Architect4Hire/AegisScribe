namespace AegisScribe.Domain.Integration.Blizzard;

// The region -> host table from .claude/skills/add-external-sync/references/blizzard-endpoints.md.
// Each region has two hosts and they are not interchangeable: game and profile data come from
// {region}.api.blizzard.com, tokens from {region}.battle.net.
public static class BlizzardHosts
{
    // These are deliberately the REGIONAL OAuth hosts. The non-regional oauth.battle.net resolves and
    // looks like a reasonable default, but it has a documented history of intermittent 403s, so
    // external.md rules it out rather than leaving it to taste.
    private static readonly Dictionary<string, (string Api, string OAuth)> Regions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["us"] = ("https://us.api.blizzard.com", "https://us.battle.net"),
            ["eu"] = ("https://eu.api.blizzard.com", "https://eu.battle.net"),
            ["kr"] = ("https://kr.api.blizzard.com", "https://kr.battle.net"),
            ["tw"] = ("https://tw.api.blizzard.com", "https://tw.battle.net"),
            ["sea"] = ("https://sea.api.blizzard.com", "https://sea.battle.net"),
        };

    public static IReadOnlyCollection<string> SupportedRegions => Regions.Keys;

    public static bool IsSupported(string? region) =>
        !string.IsNullOrWhiteSpace(region) && Regions.ContainsKey(region);

    public static string ApiHost(string region) => Lookup(region).Api;

    public static string OAuthHost(string region) => Lookup(region).OAuth;

    private static (string Api, string OAuth) Lookup(string region) =>
        Regions.TryGetValue(region, out var hosts)
            ? hosts
            : throw new InvalidOperationException(
                $"'{region}' is not a supported Blizzard region. Supported regions are: " +
                $"{string.Join(", ", Regions.Keys)}. China is not another region value — it is a " +
                "separate integration with its own gateway host and its own credentials, and is out " +
                "of scope for this build.");
}
