namespace AegisScribe.Domain.Managers.Models.Domain;

// A community's own rank ladder — "Raider", "Trial", "Social", "Officer" (7.1).
//
// Tenant-scoped, and the distinction it carries is the one tenancy.md spends a section on:
// GuildMember.BlizzardRank is what the GAME says (0-9, straight from the Blizzard roster endpoint),
// and this is what the COMMUNITY says. Neither derives from the other, and neither writes to the
// other — two communities following the same guild will disagree about who is a Raider, and both are
// right. That disagreement is exactly the test that puts this in the tenant-scoped zone.
public class TenantRank : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    // Where the rank sits in the ladder, ascending. Not unique: two ranks sharing a position is a
    // display tie, not a data error, and the list read breaks it by name.
    public int SortOrder { get; set; }

    // #rrggbb, validated at the edge. It is tenant CONFIG, not a design token: the Angular rank pill
    // surfaces it as the --rank-color custom property (design/aegisscribe-armory.html), so a value
    // that is not exactly six hex digits behind a # would be CSS injected through a settings form.
    // The format rule in RankValidationRules is that guard, not a tidiness preference.
    public string Colour { get; set; } = string.Empty;
}
