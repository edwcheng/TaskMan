using TaskMan.Domain.Users;

namespace TaskMan.Tests.Users;

public sealed class UserRulesTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("sla/sh")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_usernames_are_rejected(string? userName)
    {
        Assert.NotNull(UserRules.ValidateUserName(userName));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("first.last")]
    [InlineData("user_name-1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    public void Valid_usernames_are_accepted(string userName)
    {
        Assert.Null(UserRules.ValidateUserName(userName));
    }

    [Fact]
    public void Usernames_are_limited_to_32_characters()
    {
        var tooLong = new string('a', UserRules.UserNameMaxLength + 1);

        Assert.NotNull(UserRules.ValidateUserName(tooLong));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_display_names_are_rejected(string? displayName)
    {
        Assert.NotNull(UserRules.ValidateDisplayName(displayName));
    }

    [Fact]
    public void Display_names_are_limited_to_100_characters()
    {
        Assert.NotNull(UserRules.ValidateDisplayName(
            new string('a', UserRules.DisplayNameMaxLength + 1)));
        Assert.Null(UserRules.ValidateDisplayName(
            new string('a', UserRules.DisplayNameMaxLength)));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    public void Short_passwords_are_rejected(string? password)
    {
        Assert.NotNull(UserRules.ValidatePassword(password));
    }

    [Fact]
    public void Password_boundaries_are_inclusive()
    {
        Assert.Null(UserRules.ValidatePassword(new string('a', UserRules.PasswordMinLength)));
        Assert.Null(UserRules.ValidatePassword(new string('a', UserRules.PasswordMaxLength)));
        Assert.NotNull(UserRules.ValidatePassword(
            new string('a', UserRules.PasswordMaxLength + 1)));
    }

    [Fact]
    public void Usernames_normalize_by_trimming_and_upper_casing()
    {
        Assert.Equal("FIRST.LAST", UserRules.NormalizeUserName("  first.last  "));
    }
}
