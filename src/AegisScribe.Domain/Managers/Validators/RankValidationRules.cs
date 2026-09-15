using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

// The shape rules a create and an update of a TenantRank have in common — one place to change the
// format instead of two, the same job CharacterValidationRules does for realm/region.
internal static class RankValidationRules
{
    // Mirrors TenantRank.Name's HasMaxLength(32). Short on purpose: the rank renders inside a pill
    // next to a character name, and a 200-character rank is a broken roster row, not a long label.
    public static IRuleBuilderOptions<T, string> RankName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(32);

    // A security control, not a formatting preference: the value is written into the rank pill's
    // --rank-color custom property, so anything other than exactly #rrggbb is CSS injected through a
    // settings form. Shorthand and named colours are rejected rather than normalised — relaxing a
    // validation rule later is additive, tightening one is breaking (api-contract.md).
    public static IRuleBuilderOptions<T, string> RankColour<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Matches("^#[0-9a-fA-F]{6}$")
            .WithMessage("Colour must be a six-digit hex value such as #cba76a.");

    // Bounded so the ladder stays a ladder: an unbounded range invites int.MaxValue sentinels that the
    // next reorder cannot express a position above.
    public static IRuleBuilderOptions<T, int> RankSortOrder<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(0, 999);
}
