using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Integration.Blizzard;

public static class BlizzardGatewayExtensions
{
    // The character-media call rides along with every character refresh, and must never be the reason
    // one fails: the summary and the gear are the data, the renders are decoration. So the three answers
    // collapse to two here —
    //
    //   renders, or a 404 (no renders)  → a CharacterMedia to store, possibly empty
    //   Blizzard unavailable             → null, meaning "unknown": keep whatever is already held
    //
    // Shared by the DataLayer's refresh and the sync worker so both draw that line in the same place.
    public static async Task<CharacterMedia?> TryFetchCharacterMediaAsync(
        this IBlizzardGateway gateway,
        string realmSlug,
        string characterName,
        CancellationToken ct)
    {
        try
        {
            return await gateway.FetchCharacterMediaAsync(realmSlug, characterName, ct) ?? CharacterMedia.None;
        }
        catch (BlizzardUnavailableException)
        {
            return null;
        }
    }
}
