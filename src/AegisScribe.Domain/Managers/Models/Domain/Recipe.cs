namespace AegisScribe.Domain.Managers.Models.Domain;

public class Recipe
{
    public Guid Id { get; set; }
    public long BlizzardRecipeId { get; set; }
    public string Name { get; set; } = null!;
    public Guid ProfessionId { get; set; }
    public Profession Profession { get; set; } = null!;

    // Nullable: Blizzard may reference a crafted item this catalog hasn't synced yet
    // (item sync and recipe sync are independent jobs — see Phase 12.1). A recipe
    // should not fail to persist because its output item isn't cataloged yet.
    public Guid? CraftedItemId { get; set; }
    public Item? CraftedItem { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }
}
