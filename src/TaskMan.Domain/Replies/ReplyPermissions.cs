using TaskMan.Domain.Entities;

namespace TaskMan.Domain.Replies;

public static class ReplyPermissions
{
    // The reply's own author or an administrator may edit or delete the reply.
    public static bool CanEdit(Reply reply, Guid userId, bool isAdmin) =>
        isAdmin || reply.AuthorId == userId;
}
