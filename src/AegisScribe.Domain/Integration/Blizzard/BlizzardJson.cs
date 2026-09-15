using System.Text.Json;

namespace AegisScribe.Domain.Integration.Blizzard;

// Blizzard's JSON is snake_case throughout (character_class, equipped_item_level, active_spec), so one
// naming policy here spares every response record a [JsonPropertyName] on every property — and spares
// the next person the bug where a forgotten attribute silently deserializes to 0.
//
// Internal, like the response records it serves: nothing outside this folder deserializes Blizzard.
internal static class BlizzardJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
