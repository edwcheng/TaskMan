using TaskMan.Domain.Entities;

namespace TaskMan.Domain.Attachments;

public static class AttachmentPermissions
{
    // The uploader, the author of the parent task or reply, or an administrator
    // may delete an attachment. Anyone may upload to any task or reply.
    public static bool CanDelete(
        Attachment attachment,
        Guid parentAuthorId,
        Guid userId,
        bool isAdmin) =>
        isAdmin || attachment.UploadedById == userId || parentAuthorId == userId;
}
