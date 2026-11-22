namespace TaskMan.Domain.Attachments;

public static class AttachmentRules
{
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;
    public const int MaxAttachmentsPerParent = 10;
    public const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 150;

    public const string DefaultContentType = "application/octet-stream";

    // Inline rendering is restricted to a fixed raster allowlist. Script-capable
    // types such as SVG are never served inline.
    private static readonly Dictionary<string, string> InlineTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        };

    // The original file name is kept in the database only; files on disk are named
    // after the attachment ID. GetFileName also strips any directory components a
    // client may send, so the name can never influence a filesystem path.
    public static string SanitizeFileName(string? fileName) =>
        Path.GetFileName(fileName?.Trim() ?? string.Empty);

    public static string? ValidateFileName(string? fileName)
    {
        var name = SanitizeFileName(fileName);

        if (name.Length == 0 || name is "." or "..")
        {
            return "Attachment file name is required.";
        }

        return name.Length > MaxFileNameLength
            ? $"File name must be at most {MaxFileNameLength} characters."
            : null;
    }

    public static string NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType)
            ? DefaultContentType
            : contentType.Trim();

    public static string? ValidateContentType(string? contentType)
    {
        var value = NormalizeContentType(contentType);

        return value.Length > MaxContentTypeLength
            ? $"Content type must be at most {MaxContentTypeLength} characters."
            : null;
    }

    // The effective inline type is derived from the file extension and must also
    // match the stored, client-provided content type. Neither is trusted alone.
    public static string? GetInlineContentType(string fileName, string storedContentType)
    {
        var extension = Path.GetExtension(fileName);

        if (!InlineTypes.TryGetValue(extension, out var inlineType))
        {
            return null;
        }

        return string.Equals(
            storedContentType.Trim(),
            inlineType,
            StringComparison.OrdinalIgnoreCase)
            ? inlineType
            : null;
    }
}
