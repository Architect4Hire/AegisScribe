namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Exactly one of the two is supplied: Name asks "what slug would this community get, and is it free",
// Slug asks "is this one free" after the user has edited it. One endpoint rather than two because the
// create form (5.6c) needs both answers in one round trip per keystroke.
public class SlugCheckViewModel
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
}
