namespace TaskMan.Infrastructure.Storage;

public interface IAttachmentStore
{
    /// <summary>
    /// Streams the upload to its final sharded location and returns the number of
    /// bytes written. Throws <see cref="AttachmentTooLargeException"/> once the size
    /// limit is exceeded; the temporary file is removed on any failure.
    /// </summary>
    Task<long> SaveAsync(
        Stream source,
        Guid attachmentId,
        long maxBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the stored file, or returns <c>null</c> when it is missing.</summary>
    FileStream? OpenRead(Guid attachmentId);

    /// <summary>Removes the stored file. Missing files are ignored.</summary>
    void Delete(Guid attachmentId);
}
