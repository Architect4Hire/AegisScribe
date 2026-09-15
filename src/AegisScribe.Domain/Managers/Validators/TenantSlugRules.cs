using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;

namespace AegisScribe.Domain.Managers.Validators;

// A community's slug is its URL for the life of the community, so deriving and vetting one are domain
// rules with exactly one home. Deliberately NOT reimplemented in TypeScript or in the MAUI client —
// both get this by asking the API, which is why GET /api/v1/tenants/slug-check exists at all.
//
// Public rather than internal because Derive's fold behaviour is worth asserting directly instead of
// through seven round-trips of the create endpoint.
public static class TenantSlugRules
{
    // Mirrors Tenant.Slug's HasMaxLength(64) in OnModelCreating. Narrowing an accepted input would be a
    // breaking change (api-contract.md).
    public const int MaxLength = 64;

    // Three is the shortest slug worth having in a URL, and it removes the need for a second rule
    // reserving every one- and two-character slug separately.
    public const int MinLength = 3;

    public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    private static readonly Regex WellFormed = new(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Words that must never become a community's slug, because a route already means something else by
    // them or soon will — a community slugged "new" collides with the create screen at /t/new.
    // Reserving costs nothing now; migrating a community's URL later costs them every shared link.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "api", "assets", "auth", "characters", "connect", "create", "edit", "health",
        "invitations", "join", "login", "logout", "me", "new", "openapi", "platform", "register",
        "signin", "signout", "static", "swagger", "t", "tenants", "well-known",
    };

    public static bool IsReserved(string? slug) => slug is not null && Reserved.Contains(slug);

    public static bool IsWellFormed(string? slug) =>
        slug is not null
        && slug.Length is >= MinLength and <= MaxLength
        && WellFormed.IsMatch(slug);

    // Best effort, and null is a real answer. A name with nothing sluggable in it — pure punctuation, or
    // a script that does not transliterate, which is every Korean and Chinese community name — gets no
    // suggestion rather than a fabricated "community-a1b2". A slug nobody chose is still their URL
    // forever, so the caller asks them to type one instead.
    public static string? Derive(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // FormD splits an accented letter into base letter + combining mark, so dropping the marks
        // leaves the ASCII letter behind: "Ashés" folds to "ashes", not to "ash-s".
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                // Any run of unusable characters collapses to one separator. The guard keeps a leading
                // hyphen and doubled hyphens from ever being written, rather than trimming them after.
                builder.Append('-');
            }
        }

        var slug = builder.ToString();
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength];
        }

        // Truncation can land on the separator, and a trailing run of punctuation leaves one too.
        slug = slug.TrimEnd('-');

        return slug.Length >= MinLength ? slug : null;
    }

    public static IRuleBuilderOptions<T, string?> SlugFormat<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength)
            .MaximumLength(MaxLength)
            .Matches(Pattern)
            .Must(slug => !IsReserved(slug))
            .WithMessage("'{PropertyName}' is reserved.");
}
