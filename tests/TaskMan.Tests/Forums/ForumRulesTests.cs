using TaskMan.Domain.Forums;

namespace TaskMan.Tests.Forums;

public sealed class ForumRulesTests
{
    [Theory]
    [InlineData("General Discussion", "general-discussion")]
    [InlineData("  Bugs & Issues  ", "bugs-issues")]
    [InlineData("Café Déjà Vu", "cafe-deja-vu")]
    [InlineData("v2.0 / Rollout", "v2-0-rollout")]
    [InlineData("---", "forum")]
    [InlineData("日本語", "forum")]
    public void Slugify_produces_a_stable_url_slug(string name, string expected)
    {
        Assert.Equal(expected, ForumRules.Slugify(name));
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
