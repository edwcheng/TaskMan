using Microsoft.Extensions.Logging.Abstractions;
using TaskMan.Domain.Attachments;
using TaskMan.Infrastructure.Storage;

namespace TaskMan.Tests.Attachments;

public sealed class AttachmentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly DiskAttachmentStore _store;

    public AttachmentStoreTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "taskman-store-tests",
            Guid.NewGuid().ToString("N"));

        _store = new DiskAttachmentStore(_root, NullLogger<DiskAttachmentStore>.Instance);
    }

    [Fact]
    public async Task Save_writes_to_the_sharded_path_and_reports_the_size()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4 };

        using var source = new MemoryStream(bytes);
        var written = await _store.SaveAsync(
            source,
            id,
            AttachmentRules.MaxFileSizeBytes);

        Assert.Equal(4, written);

        var name = id.ToString("N");
        var expected = Path.Combine(_root, name[..2], name);

        Assert.Equal(bytes, File.ReadAllBytes(expected));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_over_the_limit_throws_and_leaves_no_files()
    {
        var id = Guid.NewGuid();

        using var source = new MemoryStream(new byte[3]);
        Assert.Throws<AttachmentTooLargeException>(
            () => _store.SaveAsync(source, id, maxBytes: 2).GetAwaiter().GetResult());

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenRead_returns_null_when_the_file_is_missing()
    {
        Assert.Null(_store.OpenRead(Guid.NewGuid()));
    }

    [Fact]
    public async Task OpenRead_streams_the_saved_content()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[] { 9, 8, 7 };

        using (var source = new MemoryStream(bytes))
        {
            await _store.SaveAsync(source, id, AttachmentRules.MaxFileSizeBytes);
        }

        using var stream = _store.OpenRead(id);
        Assert.NotNull(stream);

        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Delete_removes_the_file_and_is_idempotent()
    {
        var id = Guid.NewGuid();

        using (var source = new MemoryStream(new byte[] { 1 }))
        {
            await _store.SaveAsync(source, id, AttachmentRules.MaxFileSizeBytes);
        }

        var name = id.ToString("N");
        var expected = Path.Combine(_root, name[..2], name);

        _store.Delete(id);
        Assert.False(File.Exists(expected));

        _store.Delete(id);
        _store.Delete(Guid.NewGuid());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // The test never created the temporary directory.
        }
    }
}
