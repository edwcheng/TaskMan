namespace TaskMan.Domain.Entities;

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? TaskId { get; set; }

    public Guid? ReplyId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public Guid UploadedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
