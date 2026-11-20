namespace TaskMan.Domain.Entities;

public sealed class Reply
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TaskId { get; set; }

    public Guid AuthorId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
