using TaskMan.Domain.Enums;

namespace TaskMan.Domain.Entities;

// Named TaskItem so it never collides with System.Threading.Tasks.Task.
public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long Number { get; set; }

    public Guid ForumId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public string? Tag { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid AuthorId { get; set; }

    public Guid? AssigneeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
}
