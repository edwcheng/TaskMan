using TaskMan.Domain.Forums;

namespace TaskMan.Tests.Forums;

public sealed class ForumRulesTests
{
    [Theory]
    [InlineData("General Discussion", "general-discussion")]
    [InlineData("  Bugs & Issues  ", "bugs-issues")]
    [InlineData("Café Déjà Vu", "cafe-deja-vu")]
    [InlineData("v2.0 / Rollout", "v2-0-rollout")]
    public void Slugify_produces_a_stable_url_slug(string name, string expected)
    {
        Assert.Equal(expected, ForumRules.Slugify(name));
    }

    [Fact]
    public void Slugify_falls_back_to_a_hashed_slug_when_no_ascii_remains()
    {
        // Names with no ASCII letters/digits (punctuation, CJK, blank) get a
        // deterministic, URL-safe, distinct slug so they do not collide on "forum".
        var dash = ForumRules.Slugify("---");
        var cjk = ForumRules.Slugify("日本語");

        Assert.StartsWith("forum-", dash);
        Assert.StartsWith("forum-", cjk);
        Assert.NotEqual(dash, cjk);
        Assert.Equal(cjk, ForumRules.Slugify("日本語"));
        Assert.All(cjk, ch => Assert.True(
            char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-',
            $"'{ch}' is not URL-safe"));
    }

    [Fact]
    public void Slugify_limits_the_slug_length()
    {
        var slug = ForumRules.Slugify(new string('a', 250));

        Assert.Equal(ForumRules.SlugMaxLength, slug.Length);
    }

    [Theory]
    [InlineData("general")]
    [InlineData("general-discussion")]
    [InlineData("v2-rollout")]
    [InlineData("123")]
    public void Valid_slugs_are_accepted(string slug)
    {
        Assert.Null(ForumRules.ValidateSlug(slug));
    }

    [Theory]
    [InlineData("")]
    [InlineData("General")]
    [InlineData("general discussion")]
    [InlineData("-general")]
    [InlineData("general-")]
    [InlineData("general--discussion")]
    [InlineData("general_discussion")]
    public void Invalid_slugs_are_rejected(string slug)
    {
        Assert.NotNull(ForumRules.ValidateSlug(slug));
    }

    [Fact]
    public void Name_and_description_lengths_are_enforced()
    {
        Assert.Null(ForumRules.ValidateName(new string('a', ForumRules.NameMaxLength)));
        Assert.NotNull(ForumRules.ValidateName(
            new string('a', ForumRules.NameMaxLength + 1)));
        Assert.NotNull(ForumRules.ValidateName("  "));

        Assert.Null(ForumRules.ValidateDescription(null));
        Assert.Null(ForumRules.ValidateDescription(
            new string('a', ForumRules.DescriptionMaxLength)));
        Assert.NotNull(ForumRules.ValidateDescription(
            new string('a', ForumRules.DescriptionMaxLength + 1)));
    }
}
