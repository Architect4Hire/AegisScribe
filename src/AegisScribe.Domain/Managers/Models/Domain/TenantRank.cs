namespace AegisScribe.Domain.Managers.Models.Domain;

// A community's own rank ladder — "Raider", "Trial", "Social", "Officer".
//
// GuildMember.BlizzardRank is what the GAME says; this is what the COMMUNITY says, and neither derives
// from the other. Two communities following the same guild will disagree about who is a Raider and
// both be right, which is the test that puts this in the tenant-scoped zone (tenancy.md).
public class TenantRank : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    // Where the rank sits in the ladder, ascending. Not unique: two ranks sharing a position is a
    // display tie, not a data error, and the list read breaks it by name.
    public int SortOrder { get; set; }

    // #rrggbb, validated at the edge. Tenant CONFIG, not a design token: the rank pill surfaces it as
    // the --rank-color custom property, so anything but six hex digits behind a # would be CSS
    // injected through a settings form. RankValidationRules is that guard.
    public string Colour { get; set; } = string.Empty;
}
