using TaskMan.Domain.Attachments;
using TaskMan.Domain.Entities;

namespace TaskMan.Tests.Attachments;

public sealed class AttachmentRulesTests
{
    [Fact]
    public void File_names_are_sanitized_to_the_basename()
    {
        Assert.Equal("passwd", AttachmentRules.SanitizeFileName("../../etc/passwd"));
        Assert.Equal("report.pdf", AttachmentRules.SanitizeFileName("a/b/c/report.pdf"));
        Assert.Equal("report.pdf", AttachmentRules.SanitizeFileName("  report.pdf  "));
        Assert.Equal(string.Empty, AttachmentRules.SanitizeFileName(null));
    }

    [Fact]
    public void File_names_are_validated()
    {
        Assert.NotNull(AttachmentRules.ValidateFileName(null));
        Assert.NotNull(AttachmentRules.ValidateFileName(""));
        Assert.NotNull(AttachmentRules.ValidateFileName("."));
        Assert.NotNull(AttachmentRules.ValidateFileName(".."));

        // Path traversal is neutralized by sanitization, not rejected: the
        // stored name is the basename.
        Assert.Null(AttachmentRules.ValidateFileName("../../etc/passwd"));

        Assert.Null(AttachmentRules.ValidateFileName("report.pdf"));
        Assert.Null(AttachmentRules.ValidateFileName(new string('a', AttachmentRules.MaxFileNameLength)));
        Assert.NotNull(AttachmentRules.ValidateFileName(
            new string('a', AttachmentRules.MaxFileNameLength + 1) + ".pdf"));
    }

    [Fact]
    public void Content_types_are_normalized_and_validated()
    {
        Assert.Equal(
            AttachmentRules.DefaultContentType,
            AttachmentRules.NormalizeContentType(null));
        Assert.Equal(
            AttachmentRules.DefaultContentType,
            AttachmentRules.NormalizeContentType("   "));
        Assert.Equal("text/plain", AttachmentRules.NormalizeContentType(" text/plain "));

        Assert.Null(AttachmentRules.ValidateContentType(null));
        Assert.Null(AttachmentRules.ValidateContentType("image/png"));
        Assert.NotNull(AttachmentRules.ValidateContentType(
            new string('a', AttachmentRules.MaxContentTypeLength + 1)));
    }

    [Theory]
    [InlineData("photo.png", "image/png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpeg", "image/jpeg")]
    [InlineData("photo.gif", "image/gif", "image/gif")]
    [InlineData("photo.webp", "image/webp", "image/webp")]
    public void Raster_images_are_inline_allowed(
        string fileName,
        string storedContentType,
        string expected)
    {
        Assert.Equal(expected, AttachmentRules.GetInlineContentType(fileName, storedContentType));
    }

    [Theory]
    [InlineData("script.svg", "image/svg+xml")]
    [InlineData("page.html", "text/html")]
    [InlineData("video.mp4", "video/mp4")]
    [InlineData("doc.pdf", "application/pdf")]
    public void Script_capable_and_other_types_are_inline_rejected(
        string fileName,
        string storedContentType)
    {
        Assert.Null(AttachmentRules.GetInlineContentType(fileName, storedContentType));
    }

    [Theory]
    [InlineData("photo.png", "image/jpeg")]
    [InlineData("photo.png", "text/plain")]
    [InlineData("photo.jpg", "image/png")]
    public void Extension_and_content_type_must_agree(
        string fileName,
        string storedContentType)
    {
        Assert.Null(AttachmentRules.GetInlineContentType(fileName, storedContentType));
    }

    [Fact]
    public void Permission_helpers_match_the_matrix()
    {
        var uploaderId = Guid.NewGuid();
        var parentAuthorId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var attachment = new Attachment
        {
            UploadedById = uploaderId,
        };

        Assert.True(AttachmentPermissions.CanDelete(attachment, parentAuthorId, uploaderId, isAdmin: false));
        Assert.True(AttachmentPermissions.CanDelete(attachment, parentAuthorId, parentAuthorId, isAdmin: false));
        Assert.True(AttachmentPermissions.CanDelete(attachment, parentAuthorId, otherId, isAdmin: true));
        Assert.False(AttachmentPermissions.CanDelete(attachment, parentAuthorId, otherId, isAdmin: false));
    }
}
