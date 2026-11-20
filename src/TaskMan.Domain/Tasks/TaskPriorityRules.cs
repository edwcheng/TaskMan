using TaskMan.Domain.Enums;

namespace TaskMan.Domain.Tasks;

public static class TaskPriorityRules
{
    public static int Rank(TaskPriority priority) =>
        priority switch
        {
            TaskPriority.Low => 1,
            TaskPriority.Normal => 2,
            TaskPriority.High => 3,
            TaskPriority.Urgent => 4,
            _ => 0,
        };
}
