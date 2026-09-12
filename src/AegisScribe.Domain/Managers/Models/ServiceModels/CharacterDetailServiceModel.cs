using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class CharacterDetailServiceModel
{
    public Guid Id { get; set; }
    public string RealmSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public CharacterClass Class { get; set; }
    public string? Spec { get; set; }
    public int ItemLevel { get; set; }
    public CharacterFaction Faction { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
    public IReadOnlyList<EquippedItemServiceModel> Equipment { get; set; } = [];

    // Hex, resolved server-side from Class so the frontend never carries a class-to-colour map —
    // see .claude/skills/aegisscribe-design-system/references/tokens.md's --c-* tokens, which this
    // mirrors exactly.
    public string ClassColor { get; set; } = string.Empty;

    // Always false today: nothing before Phase 6 (the Blizzard cache-first gateway) can serve a
    // stale row. The flag exists now so the UI's degraded state isn't designed after the fact.
    public bool IsDegraded { get; set; }
}
