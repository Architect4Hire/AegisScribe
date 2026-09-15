namespace AegisScribe.Domain.Integration.Blizzard;

public interface IBlizzardTokenProvider
{
    // Returns a usable access token, or null when there is none to be had — no credentials configured,
    // or battle.net would not mint one. Never throws for either case: offline development is a
    // first-class case and missing credentials degrade rather than crash (external.md).
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}
