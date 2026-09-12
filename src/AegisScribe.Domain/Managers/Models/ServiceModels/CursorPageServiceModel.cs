namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// The cursor-pagination envelope every list endpoint returns (api-contract.md): items, an opaque
// nextCursor the client passes back unexamined, and hasMore.
public class CursorPageServiceModel<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}
