using TaskMan.Domain.Entities;

namespace TaskMan.Domain.Tasks;

public static class TaskPermissions
{
    public static bool CanEdit(TaskItem task, Guid userId, bool isAdmin) =>
        isAdmin || task.AuthorId == userId;

    public static bool CanChangeStatus(TaskItem task, Guid userId, bool isAdmin) =>
        isAdmin || task.AuthorId == userId || task.AssigneeId == userId;

    public static bool CanDelete(TaskItem task, Guid userId, bool isAdmin) =>
        CanEdit(task, userId, isAdmin);
}
