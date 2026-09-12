namespace AegisScribe.Domain.Managers.Models.Domain;

public class ReagentSlot
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int Quantity { get; set; }
}
