using TaskMan.Domain.Enums;

namespace TaskMan.Api.Contracts;

public sealed record UserSummaryResponse(
    Guid Id,
    string UserName,
    string DisplayName);

public sealed record AttachmentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    UserSummaryResponse UploadedBy,
    DateTimeOffset CreatedAt);

public sealed record TaskListItemResponse(
    Guid Id,
    long Number,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    string? Tag,
    DateOnly? DueDate,
    ForumSummaryResponse Forum,
    UserSummaryResponse Author,
    UserSummaryResponse? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int ReplyCount,
    int AttachmentCount);

public sealed record TaskDetailResponse(
    Guid Id,
    long Number,
    string Title,
    string Body,
    TaskItemStatus Status,
    TaskPriority Priority,
    string? Tag,
    DateOnly? DueDate,
    ForumSummaryResponse Forum,
    UserSummaryResponse Author,
    UserSummaryResponse? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActivityAt,
    int ReplyCount,
    IReadOnlyList<AttachmentResponse> Attachments);

public sealed record CreateTaskRequest(
    Guid? ForumId,
    string? Title,
    string? Body,
    TaskItemStatus? Status,
    TaskPriority? Priority,
    string? Tag,
    DateOnly? DueDate,
    Guid? AssigneeId);

public sealed record UpdateTaskRequest(
    Guid? ForumId,
    string? Title,
    string? Body,
    TaskItemStatus? Status,
    TaskPriority? Priority,
    string? Tag,
    DateOnly? DueDate,
    Guid? AssigneeId);

public sealed record UpdateTaskStatusRequest(TaskItemStatus? Status);

public sealed class TaskListQuery
{
    public TaskItemStatus[]? Status { get; set; }

    public TaskPriority[]? Priority { get; set; }

    public string? AssigneeId { get; set; }

    public string? Tag { get; set; }

    public string? Q { get; set; }

    public DateOnly? DueBefore { get; set; }

    public DateOnly? DueAfter { get; set; }

    public bool? Overdue { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public string? Sort { get; set; }
}
