using AegisScribe.Domain.Managers.Validators;

namespace AegisScribe.Tests.Tenancy;

// The fold cases (2.7b). These are pure functions with no data dependency, so they're asserted
// directly rather than through seven round-trips of the create endpoint.
public class TenantSlugRulesTests
{
    [Theory]
    [InlineData("Ashes of Dawn", "ashes-of-dawn")]
    [InlineData("  Ashes of Dawn  ", "ashes-of-dawn")]
    // Accents fold to their base letter rather than becoming separators — the whole reason Derive
    // normalizes to FormD before filtering.
    [InlineData("Ashés of Dawn", "ashes-of-dawn")]
    [InlineData("Åsgard Ünited", "asgard-united")]
    // A run of punctuation collapses to one separator, and leading/trailing runs produce none.
    [InlineData("Ashes   of //// Dawn", "ashes-of-dawn")]
    [InlineData("!!!Ashes of Dawn!!!", "ashes-of-dawn")]
    [InlineData("Ashes - of - Dawn", "ashes-of-dawn")]
    [InlineData("<Ashes of Dawn>", "ashes-of-dawn")]
    [InlineData("Ashes 0f D4wn", "ashes-0f-d4wn")]
    [InlineData("ALL CAPS GUILD", "all-caps-guild")]
    // Exactly MinLength after folding is kept, not discarded.
    [InlineData("a b", "a-b")]
    public void Derive_FoldsToASlug(string name, string expected) =>
        Assert.Equal(expected, TenantSlugRules.Derive(name));

    [Fact]
    public void Derive_TruncatesToMaxLength()
    {
        var slug = TenantSlugRules.Derive(new string('a', TenantSlugRules.MaxLength + 20));

        Assert.Equal(new string('a', TenantSlugRules.MaxLength), slug);
    }

    [Fact]
    public void Derive_TruncationLandingOnASeparator_DoesNotLeaveATrailingHyphen()
    {
        // 63 characters, then a space: the truncation boundary falls exactly on the separator the
        // space produced, which is the case a naive substring gets wrong.
        var name = new string('a', TenantSlugRules.MaxLength - 1) + " tail";

        var slug = TenantSlugRules.Derive(name);

        Assert.NotNull(slug);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug!.EndsWith('-'), $"'{slug}' ends with a separator.");
        Assert.True(TenantSlugRules.IsWellFormed(slug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Nothing sluggable: pure punctuation, or a script that doesn't transliterate. Both get null
    // rather than a fabricated slug — a Korean or Chinese community name is a real case, not an
    // exotic one, and the form asks them to type a slug instead.
    [InlineData("!!!")]
    [InlineData("・・・")]
    [InlineData("잿빛 여명")]
    [InlineData("灰烬黎明")]
    // Shorter than MinLength after folding.
    [InlineData("Hi")]
    [InlineData("9")]
    [InlineData("ab")]
    public void Derive_NothingUsable_ReturnsNull(string? name) =>
        Assert.Null(TenantSlugRules.Derive(name));

    [Fact]
    public void Derive_AlwaysProducesAWellFormedSlug()
    {
        string[] names =
        [
            "Ashes of Dawn", "!!!Ashes!!!", "Åsgard  --  Ünited", "9 Lives", "x".PadRight(200, 'y'),
        ];

        foreach (var name in names)
        {
            var slug = TenantSlugRules.Derive(name);
            Assert.NotNull(slug);
            Assert.True(TenantSlugRules.IsWellFormed(slug), $"'{name}' derived the malformed slug '{slug}'.");
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("new")]
    [InlineData("api")]
    [InlineData("t")]
    [InlineData("tenants")]
    [InlineData("join")]
    public void IsReserved_RejectsRouteWords(string slug) => Assert.True(TenantSlugRules.IsReserved(slug));

    [Theory]
    [InlineData("ashes-of-dawn")]
    [InlineData("administrators")]
    [InlineData("newcomers")]
    [InlineData(null)]
    public void IsReserved_LeavesOrdinarySlugsAlone(string? slug) => Assert.False(TenantSlugRules.IsReserved(slug));

    [Theory]
    [InlineData("ashes-of-dawn")]
    [InlineData("abc")]
    [InlineData("a1b2")]
    public void IsWellFormed_AcceptsValidSlugs(string slug) => Assert.True(TenantSlugRules.IsWellFormed(slug));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]              // shorter than MinLength
    [InlineData("Ashes")]           // uppercase
    [InlineData("ashes of dawn")]   // spaces
    [InlineData("-ashes")]          // leading separator
    [InlineData("ashes-")]          // trailing separator
    [InlineData("ashes--dawn")]     // doubled separator
    [InlineData("ashes_of_dawn")]   // underscore
    public void IsWellFormed_RejectsTheRest(string? slug) => Assert.False(TenantSlugRules.IsWellFormed(slug));

    [Fact]
    public void IsWellFormed_RejectsOverMaxLength() =>
        Assert.False(TenantSlugRules.IsWellFormed(new string('a', TenantSlugRules.MaxLength + 1)));
}
