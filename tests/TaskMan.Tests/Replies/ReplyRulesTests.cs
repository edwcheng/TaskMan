using TaskMan.Domain.Entities;
using TaskMan.Domain.Replies;

namespace TaskMan.Tests.Replies;

public sealed class ReplyRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_bodies_are_rejected(string? body)
    {
        Assert.NotNull(ReplyRules.ValidateBody(body));
    }

    [Fact]
    public void Body_length_is_enforced()
    {
        Assert.Null(ReplyRules.ValidateBody("A reply"));
        Assert.Null(ReplyRules.ValidateBody(new string('a', ReplyRules.BodyMaxLength)));
        Assert.NotNull(ReplyRules.ValidateBody(
            new string('a', ReplyRules.BodyMaxLength + 1)));
    }

    [Fact]
    public void Permission_helpers_match_the_matrix()
    {
        var authorId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var reply = new Reply
        {
            AuthorId = authorId,
        };

        Assert.True(ReplyPermissions.CanEdit(reply, authorId, isAdmin: false));
        Assert.True(ReplyPermissions.CanEdit(reply, otherId, isAdmin: true));
        Assert.False(ReplyPermissions.CanEdit(reply, otherId, isAdmin: false));
    }
}
