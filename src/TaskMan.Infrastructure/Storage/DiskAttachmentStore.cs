using Microsoft.Extensions.Logging;

namespace TaskMan.Infrastructure.Storage;

public sealed class DiskAttachmentStore(
    string rootPath,
    ILogger<DiskAttachmentStore> logger) : IAttachmentStore
{
    private const int BufferSize = 81_920;

    public async Task<long> SaveAsync(
        Stream source,
        Guid attachmentId,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var name = attachmentId.ToString("N");
        var shardDirectory = Path.Combine(rootPath, name[..2]);
        Directory.CreateDirectory(shardDirectory);

        var finalPath = Path.Combine(shardDirectory, name);
        var tempPath = Path.Combine(shardDirectory, name + ".tmp");

        try
        {
            using var temp = File.Create(tempPath);

            var buffer = new byte[BufferSize];
            var written = 0L;

            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                written += read;

                if (written > maxBytes)
                {
                    throw new AttachmentTooLargeException(maxBytes);
                }

                temp.Write(buffer.AsSpan(0, read));
            }

            await temp.FlushAsync(cancellationToken);

            File.Move(tempPath, finalPath, overwrite: true);

            return written;
        }
        catch (AttachmentTooLargeException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception)
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public FileStream? OpenRead(Guid attachmentId)
    {
        var path = FinalPath(attachmentId);

        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            // The file was removed between the caller's checks; report it as
            // missing so the caller can answer 404.
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            // Same case where the shard directory was never created.
            return null;
        }
    }

    public void Delete(Guid attachmentId)
    {
        var path = FinalPath(attachmentId);

        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
            // A missing file is already an orphan; nothing to remove.
        }
        catch (Exception ex)
        {
            // The metadata row is already gone, so the remaining file is a harmless
            // orphan that the report job can surface later.
            logger.LogWarning(
                ex,
                "Failed to remove attachment file {Path}; an orphan file remains.",
                path);
        }
    }

    private string FinalPath(Guid attachmentId)
    {
        var name = attachmentId.ToString("N");
        return Path.Combine(rootPath, name[..2], name);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
            // Already gone.
        }
        catch (DirectoryNotFoundException)
        {
            // The shard directory was never created.
        }
    }
}
