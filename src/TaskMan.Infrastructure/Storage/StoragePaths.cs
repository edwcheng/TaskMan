using Microsoft.Extensions.Configuration;

namespace TaskMan.Infrastructure.Storage;

public sealed class StoragePaths
{
    private StoragePaths(string attachmentsPath, string keysPath)
    {
        AttachmentsPath = attachmentsPath;
        KeysPath = keysPath;
    }

    public string AttachmentsPath { get; }

    public string KeysPath { get; }

    public static StoragePaths Create(IConfiguration configuration, string contentRootPath)
    {
        var attachments = Resolve(
            configuration["Storage:AttachmentsPath"] ?? "/data/attachments",
            contentRootPath);

        var keys = Resolve(
            configuration["Storage:KeysPath"] ?? "/data/keys",
            contentRootPath);

        return new StoragePaths(attachments, keys);
    }

    public void EnsureWritable()
    {
        EnsureWritableDirectory(AttachmentsPath, "Storage:AttachmentsPath");
        EnsureWritableDirectory(KeysPath, "Storage:KeysPath");
    }

    private static string Resolve(string configuredPath, string contentRootPath) =>
        Path.GetFullPath(configuredPath, contentRootPath);

    private static void EnsureWritableDirectory(string path, string settingName)
    {
        try
        {
            Directory.CreateDirectory(path);

            var probePath = Path.Combine(path, $".write-check-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The directory configured by {settingName} ('{path}') is not writable.",
                ex);
        }
    }
}
