using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

// Shared shape rules for the region/realm-slug fields every character-lookup-style ViewModel
// carries — one place to change the format instead of three (code review, 4.6).
internal static class CharacterValidationRules
{
    public static IRuleBuilderOptions<T, string?> RegionFormat<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MaximumLength(8).Matches("^[a-z]+$");

    // Same slug shape as CreateTenantViewModelValidator — mirrors Realm.Slug's HasMaxLength(64).
    // NotEmpty is deliberately not included here: whether a realm slug is required differs by
    // caller (a lookup needs one, a search's realm filter is optional).
    public static IRuleBuilderOptions<T, string?> RealmSlugFormat<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(64).Matches("^[a-z0-9]+(-[a-z0-9]+)*$");
}
