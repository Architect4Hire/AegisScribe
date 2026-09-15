namespace AegisScribe.Domain.Managers.Models.ViewModels;

// What an officer supplies to link a guild. No TenantId — it is resolved from the route and validated
// against membership (tenancy.md), and a client-supplied one would be a privilege escalation with a
// friendly name.
public class LinkGuildViewModel
{
    public string Region { get; set; } = "us";

    public string RealmSlug { get; set; } = string.Empty;

    // The display name, not a slug. Blizzard's slug rules are ours to apply, not the caller's to know.
    public string GuildName { get; set; } = string.Empty;
}
