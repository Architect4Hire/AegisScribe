namespace AegisScribe.Domain.Managers.Models.Domain;

// What the character-media endpoint gives this app: the small square avatar for the banner and the
// full-body render for the profile's centre column. URLs on render.worldofwarcraft.com, referenced
// directly by the client — never proxied or re-hosted (blizzard-endpoints.md → "Images and media").
//
// Either can be null, and both together is a normal answer: a character can have no renders at all,
// which is why the UI's silhouette and initial fallbacks are designed states rather than error handling.
public sealed record CharacterMedia(string? AvatarUrl, string? RenderUrl)
{
    public static readonly CharacterMedia None = new(null, null);
}
