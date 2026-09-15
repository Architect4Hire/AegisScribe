namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The name only. The guild and the rank are route segments, because they identify WHICH rank is being
// named rather than being part of the change — and because a body that could name a guild would be a
// way to write a row about a guild this community does not follow.
public class SetGuildRankNameViewModel
{
    public string? Name { get; set; }
}
