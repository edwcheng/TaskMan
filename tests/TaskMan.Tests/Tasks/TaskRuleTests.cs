using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Tasks;

namespace TaskMan.Tests.Tasks;

public sealed class TaskRuleTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_titles_are_rejected(string? title)
    {
        Assert.NotNull(TaskRules.ValidateTitle(title));
    }

    [Fact]
    public void Title_body_and_tag_lengths_are_enforced()
    {
        Assert.Null(TaskRules.ValidateTitle(new string('a', TaskRules.TitleMaxLength)));
        Assert.NotNull(TaskRules.ValidateTitle(
            new string('a', TaskRules.TitleMaxLength + 1)));

        Assert.Null(TaskRules.ValidateBody(string.Empty));
        Assert.Null(TaskRules.ValidateBody(new string('a', TaskRules.BodyMaxLength)));
        Assert.NotNull(TaskRules.ValidateBody(null));
        Assert.NotNull(TaskRules.ValidateBody(
            new string('a', TaskRules.BodyMaxLength + 1)));

        Assert.Null(TaskRules.ValidateTag(null));
        Assert.Null(TaskRules.ValidateTag(new string('a', TaskRules.TagMaxLength)));
        Assert.NotNull(TaskRules.ValidateTag(
            new string('a', TaskRules.TagMaxLength + 1)));
    }

    [Fact]
    public void Tags_are_trimmed_and_blank_tags_become_null()
    {
        Assert.Equal("release", TaskRules.NormalizeTag("  release  "));
        Assert.Null(TaskRules.NormalizeTag("  "));
        Assert.Null(TaskRules.NormalizeTag(null));
    }

    [Theory]
    [InlineData(TaskItemStatus.Open, true)]
    [InlineData(TaskItemStatus.InProgress, true)]
    [InlineData(TaskItemStatus.OnHold, true)]
    [InlineData(TaskItemStatus.Completed, false)]
    [InlineData(TaskItemStatus.Closed, false)]
    public void Open_statuses_are_identified(TaskItemStatus status, bool expected)
    {
        Assert.Equal(expected, TaskRules.IsOpen(status));
    }

    [Fact]
    public void Priority_rank_orders_urgent_highest()
    {
        Assert.True(TaskPriorityRules.Rank(TaskPriority.Urgent)
            > TaskPriorityRules.Rank(TaskPriority.High));
        Assert.True(TaskPriorityRules.Rank(TaskPriority.High)
            > TaskPriorityRules.Rank(TaskPriority.Normal));
        Assert.True(TaskPriorityRules.Rank(TaskPriority.Normal)
            > TaskPriorityRules.Rank(TaskPriority.Low));
    }

    [Fact]
    public void Permission_helpers_match_the_matrix()
    {
        var authorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var task = new TaskItem
        {
            AuthorId = authorId,
            AssigneeId = assigneeId,
        };

        Assert.True(TaskPermissions.CanEdit(task, authorId, isAdmin: false));
        Assert.True(TaskPermissions.CanDelete(task, authorId, isAdmin: false));
        Assert.True(TaskPermissions.CanChangeStatus(task, authorId, isAdmin: false));

        Assert.False(TaskPermissions.CanEdit(task, assigneeId, isAdmin: false));
        Assert.False(TaskPermissions.CanDelete(task, assigneeId, isAdmin: false));
        Assert.True(TaskPermissions.CanChangeStatus(task, assigneeId, isAdmin: false));

        Assert.False(TaskPermissions.CanEdit(task, otherId, isAdmin: false));
        Assert.False(TaskPermissions.CanDelete(task, otherId, isAdmin: false));
        Assert.False(TaskPermissions.CanChangeStatus(task, otherId, isAdmin: false));

        Assert.True(TaskPermissions.CanEdit(task, otherId, isAdmin: true));
        Assert.True(TaskPermissions.CanDelete(task, otherId, isAdmin: true));
        Assert.True(TaskPermissions.CanChangeStatus(task, otherId, isAdmin: true));
    }
}
