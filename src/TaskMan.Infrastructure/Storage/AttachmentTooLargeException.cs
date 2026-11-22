namespace TaskMan.Infrastructure.Storage;

public sealed class AttachmentTooLargeException(long limitBytes)
    : InvalidOperationException(
        $"The uploaded file exceeds the {limitBytes} byte limit.")
{
    public long LimitBytes { get; } = limitBytes;
}
