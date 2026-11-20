namespace TaskMan.Domain.Entities;

public sealed class Forum
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
