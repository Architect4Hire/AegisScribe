namespace AegisScribe.Domain.Managers.Models.ViewModels;

public class SearchCharactersViewModel
{
    public string Region { get; set; } = string.Empty;
    public string? RealmSlug { get; set; }
    public string? Name { get; set; }
    public string? AfterNameLower { get; set; }
    public Guid? AfterId { get; set; }
    public int Limit { get; set; } = 25;
}
