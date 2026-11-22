using TaskMan.Domain.Enums;

namespace TaskMan.Domain.Tasks;

public static class TaskRules
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 100_000;
    public const int TagMaxLength = 50;

    public static string? ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Task title is required.";
        }

        return title.Trim().Length > TitleMaxLength
            ? $"Task title must be at most {TitleMaxLength} characters."
            : null;
    }

    public static string? ValidateBody(string? body)
    {
        if (body is null)
        {
            return "Task body is required.";
        }

        return body.Length > BodyMaxLength
            ? $"Task body must be at most {BodyMaxLength} characters."
            : null;
    }

    public static string? ValidateTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return tag.Trim().Length > TagMaxLength
            ? $"Tag must be at most {TagMaxLength} characters."
            : null;
    }

    // Tags are stored lower-cased so that filtering is an exact, case-insensitive
    // comparison on the column (index-friendly) rather than a function over it.
    public static string? NormalizeTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();

    public static bool IsOpen(TaskItemStatus status) =>
        status is not TaskItemStatus.Completed and not TaskItemStatus.Closed;
}
