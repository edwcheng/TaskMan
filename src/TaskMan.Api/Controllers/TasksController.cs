using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskMan.Api.Contracts;
using TaskMan.Api.Security;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Tasks;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize]
public sealed class TasksController(AppDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResponse<TaskListItemResponse>>> List(
        [FromQuery] TaskListQuery query,
        CancellationToken cancellationToken) =>
        await ListAsync(db.Tasks.AsNoTracking(), query, cancellationToken);

    [HttpGet("/api/forums/{forumId:guid}/tasks")]
    public async Task<ActionResult<PagedResponse<TaskListItemResponse>>> ListByForum(
        Guid forumId,
        [FromQuery] TaskListQuery query,
        CancellationToken cancellationToken)
    {
        if (!await db.Forums.AnyAsync(x => x.Id == forumId, cancellationToken))
        {
            return NotFoundProblem("Forum not found", "The requested forum does not exist.");
        }

        return await ListAsync(
            db.Tasks.AsNoTracking().Where(x => x.ForumId == forumId),
            query,
            cancellationToken);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskDetailResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var task = await LoadTaskDetailAsync(id, cancellationToken);

        return task is null
            ? NotFoundProblem("Task not found", "The requested task does not exist.")
            : Ok(task);
    }

    [HttpPost]
    public async Task<ActionResult<TaskDetailResponse>> Create(
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ForumId is not { } forumId)
        {
            return InvalidField("forumId", "Forum is required.");
        }

        if (TaskRules.ValidateTitle(request.Title) is { } titleError)
        {
            return InvalidField("title", titleError);
        }

        if (TaskRules.ValidateBody(request.Body) is { } bodyError)
        {
            return InvalidField("body", bodyError);
        }

        if (TaskRules.ValidateTag(request.Tag) is { } tagError)
        {
            return InvalidField("tag", tagError);
        }

        var status = request.Status ?? TaskItemStatus.Open;
        var priority = request.Priority ?? TaskPriority.Normal;

        if (!Enum.IsDefined(status))
        {
            return InvalidField("status", "Status is invalid.");
        }

        if (!Enum.IsDefined(priority))
        {
            return InvalidField("priority", "Priority is invalid.");
        }

        var forum = await db.Forums.SingleOrDefaultAsync(
            x => x.Id == forumId,
            cancellationToken);

        if (forum is null)
        {
            return NotFoundProblem("Forum not found", "The requested forum does not exist.");
        }

        if (forum.IsArchived)
        {
            return BadRequestProblem("Archived forums cannot receive new tasks.");
        }

        if (request.AssigneeId is { } assigneeId
            && !await IsActiveUserAsync(assigneeId, cancellationToken))
        {
            return InvalidField("assigneeId", "Assignee must be an active user.");
        }

        var now = DateTimeOffset.UtcNow;

        var task = new TaskItem
        {
            ForumId = forumId,
            Title = request.Title!.Trim(),
            Body = request.Body!,
            Status = status,
            Priority = priority,
            Tag = TaskRules.NormalizeTag(request.Tag),
            DueDate = request.DueDate,
            AuthorId = User.GetUserId(),
            AssigneeId = request.AssigneeId,
            CreatedAt = now,
            UpdatedAt = now,
            LastActivityAt = now,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        var response = await LoadTaskDetailAsync(task.Id, cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = task.Id },
            response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskDetailResponse>> Update(
        Guid id,
        UpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);

        if (task is null)
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        if (!TaskPermissions.CanEdit(task, User.GetUserId(), User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        if (request.ForumId is not { } forumId)
        {
            return InvalidField("forumId", "Forum is required.");
        }

        if (TaskRules.ValidateTitle(request.Title) is { } titleError)
        {
            return InvalidField("title", titleError);
        }

        if (TaskRules.ValidateBody(request.Body) is { } bodyError)
        {
            return InvalidField("body", bodyError);
        }

        if (TaskRules.ValidateTag(request.Tag) is { } tagError)
        {
            return InvalidField("tag", tagError);
        }

        if (request.Status is not { } status || !Enum.IsDefined(status))
        {
            return InvalidField("status", "Status is required and must be valid.");
        }

        if (request.Priority is not { } priority || !Enum.IsDefined(priority))
        {
            return InvalidField("priority", "Priority is required and must be valid.");
        }

        var forum = await db.Forums.SingleOrDefaultAsync(
            x => x.Id == forumId,
            cancellationToken);

        if (forum is null)
        {
            return NotFoundProblem("Forum not found", "The requested forum does not exist.");
        }

        if (forum.Id != task.ForumId && forum.IsArchived)
        {
            return BadRequestProblem("Tasks cannot be moved into an archived forum.");
        }

        if (request.AssigneeId != task.AssigneeId
            && request.AssigneeId is { } assigneeId
            && !await IsActiveUserAsync(assigneeId, cancellationToken))
        {
            return InvalidField("assigneeId", "Assignee must be an active user.");
        }

        task.ForumId = forumId;
        task.Title = request.Title!.Trim();
        task.Body = request.Body!;
        task.Status = status;
        task.Priority = priority;
        task.Tag = TaskRules.NormalizeTag(request.Tag);
        task.DueDate = request.DueDate;
        task.AssigneeId = request.AssigneeId;

        var now = DateTimeOffset.UtcNow;
        task.UpdatedAt = now;
        task.LastActivityAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(await LoadTaskDetailAsync(task.Id, cancellationToken));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TaskDetailResponse>> UpdateStatus(
        Guid id,
        UpdateTaskStatusRequest request,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);

        if (task is null)
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        if (!TaskPermissions.CanChangeStatus(
                task,
                User.GetUserId(),
                User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        if (request.Status is not { } status || !Enum.IsDefined(status))
        {
            return InvalidField("status", "Status is required and must be valid.");
        }

        if (task.Status != status)
        {
            task.Status = status;
            task.LastActivityAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(await LoadTaskDetailAsync(task.Id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);

        if (task is null)
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        if (!TaskPermissions.CanDelete(
                task,
                User.GetUserId(),
                User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<ActionResult<PagedResponse<TaskListItemResponse>>> ListAsync(
        IQueryable<TaskItem> tasks,
        TaskListQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Status is { Length: > 0 } statuses)
        {
            if (statuses.Any(status => !Enum.IsDefined(status)))
            {
                return InvalidField("status", "One or more statuses are invalid.");
            }

            tasks = tasks.Where(x => statuses.Contains(x.Status));
        }

        if (query.Priority is { Length: > 0 } priorities)
        {
            if (priorities.Any(priority => !Enum.IsDefined(priority)))
            {
                return InvalidField("priority", "One or more priorities are invalid.");
            }

            tasks = tasks.Where(x => priorities.Contains(x.Priority));
        }

        if (!string.IsNullOrWhiteSpace(query.AssigneeId))
        {
            var assigneeValue = query.AssigneeId.Trim();
            Guid assigneeId;

            if (string.Equals(assigneeValue, "me", StringComparison.OrdinalIgnoreCase))
            {
                assigneeId = User.GetUserId();
            }
            else if (!Guid.TryParse(assigneeValue, out assigneeId))
            {
                return InvalidField(
                    "assigneeId",
                    "Assignee filter must be a user ID or 'me'.");
            }

            tasks = tasks.Where(x => x.AssigneeId == assigneeId);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            tasks = tasks.Where(x => x.Tag != null && x.Tag.ToLower() == tag);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var pattern = $"%{term}%";

            tasks = tasks.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || EF.Functions.ILike(x.Body, pattern)
                || (x.Tag != null && EF.Functions.ILike(x.Tag, pattern)));
        }

        if (query.DueBefore is { } dueBefore)
        {
            tasks = tasks.Where(x => x.DueDate != null && x.DueDate <= dueBefore);
        }

        if (query.DueAfter is { } dueAfter)
        {
            tasks = tasks.Where(x => x.DueDate != null && x.DueDate >= dueAfter);
        }

        if (query.Overdue == true)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            tasks = tasks.Where(x =>
                x.DueDate != null
                && x.DueDate < today
                && x.Status != TaskItemStatus.Completed
                && x.Status != TaskItemStatus.Closed);
        }

        var sort = string.IsNullOrWhiteSpace(query.Sort)
            ? "activity"
            : query.Sort.Trim().ToLowerInvariant();

        if (sort is not ("activity" or "created" or "due" or "priority"))
        {
            return InvalidField(
                "sort",
                "Sort must be one of: activity, created, due, priority.");
        }

        IOrderedQueryable<TaskItem> ordered = sort switch
        {
            "created" => tasks
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Number),
            "due" => tasks
                .OrderBy(x => x.DueDate == null ? 1 : 0)
                .ThenBy(x => x.DueDate)
                .ThenByDescending(x => x.LastActivityAt),
            "priority" => tasks
                .OrderByDescending(x => x.Priority == TaskPriority.Urgent
                    ? 4
                    : x.Priority == TaskPriority.High
                        ? 3
                        : x.Priority == TaskPriority.Normal
                            ? 2
                            : 1)
                .ThenByDescending(x => x.LastActivityAt),
            _ => tasks
                .OrderByDescending(x => x.LastActivityAt)
                .ThenByDescending(x => x.Number),
        };

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaxPageSize);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var rows = await (
                from task in ordered
                join forum in db.Forums.AsNoTracking() on task.ForumId equals forum.Id
                join author in db.Users.AsNoTracking() on task.AuthorId equals author.Id
                join assignee in db.Users.AsNoTracking()
                    on task.AssigneeId equals (Guid?)assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                select new
                {
                    task.Id,
                    task.Number,
                    task.Title,
                    task.Status,
                    task.Priority,
                    task.Tag,
                    task.DueDate,
                    task.CreatedAt,
                    task.LastActivityAt,
                    ForumId = forum.Id,
                    ForumName = forum.Name,
                    ForumSlug = forum.Slug,
                    ForumIsArchived = forum.IsArchived,
                    AuthorId = author.Id,
                    AuthorUserName = author.UserName,
                    AuthorDisplayName = author.DisplayName,
                    AssigneeId = assignee == null ? (Guid?)null : assignee.Id,
                    AssigneeUserName = assignee == null ? null : assignee.UserName,
                    AssigneeDisplayName = assignee == null ? null : assignee.DisplayName,
                    ReplyCount = db.Replies.Count(x => x.TaskId == task.Id),
                    AttachmentCount = db.Attachments.Count(x => x.TaskId == task.Id),
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new TaskListItemResponse(
                row.Id,
                row.Number,
                row.Title,
                row.Status,
                row.Priority,
                row.Tag,
                row.DueDate,
                new ForumSummaryResponse(
                    row.ForumId,
                    row.ForumName,
                    row.ForumSlug,
                    row.ForumIsArchived),
                new UserSummaryResponse(
                    row.AuthorId,
                    row.AuthorUserName,
                    row.AuthorDisplayName),
                row.AssigneeId is { } assigneeId
                    ? new UserSummaryResponse(
                        assigneeId,
                        row.AssigneeUserName!,
                        row.AssigneeDisplayName!)
                    : null,
                row.CreatedAt,
                row.LastActivityAt,
                row.ReplyCount,
                row.AttachmentCount))
            .ToList();

        return Ok(new PagedResponse<TaskListItemResponse>(
            items,
            page,
            pageSize,
            totalCount));
    }

    private async Task<TaskDetailResponse?> LoadTaskDetailAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (task is null)
        {
            return null;
        }

        var forum = await db.Forums
            .AsNoTracking()
            .Where(x => x.Id == task.ForumId)
            .Select(x => new ForumSummaryResponse(
                x.Id,
                x.Name,
                x.Slug,
                x.IsArchived))
            .SingleAsync(cancellationToken);

        var author = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == task.AuthorId)
            .Select(x => new UserSummaryResponse(
                x.Id,
                x.UserName,
                x.DisplayName))
            .SingleAsync(cancellationToken);

        UserSummaryResponse? assignee = null;

        if (task.AssigneeId is { } assigneeId)
        {
            assignee = await db.Users
                .AsNoTracking()
                .Where(x => x.Id == assigneeId)
                .Select(x => new UserSummaryResponse(
                    x.Id,
                    x.UserName,
                    x.DisplayName))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var attachmentRows = await db.Attachments
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.FileName,
                x.ContentType,
                x.SizeBytes,
                x.UploadedById,
                x.CreatedAt,
                UploadedByUserName = db.Users
                    .Where(user => user.Id == x.UploadedById)
                    .Select(user => user.UserName)
                    .First(),
                UploadedByDisplayName = db.Users
                    .Where(user => user.Id == x.UploadedById)
                    .Select(user => user.DisplayName)
                    .First(),
            })
            .ToListAsync(cancellationToken);

        var attachments = attachmentRows
            .Select(x => new AttachmentResponse(
                x.Id,
                x.FileName,
                x.ContentType,
                x.SizeBytes,
                new UserSummaryResponse(
                    x.UploadedById,
                    x.UploadedByUserName,
                    x.UploadedByDisplayName),
                x.CreatedAt))
            .ToList();

        var replyCount = await db.Replies
            .AsNoTracking()
            .CountAsync(x => x.TaskId == task.Id, cancellationToken);

        return new TaskDetailResponse(
            task.Id,
            task.Number,
            task.Title,
            task.Body,
            task.Status,
            task.Priority,
            task.Tag,
            task.DueDate,
            forum,
            author,
            assignee,
            task.CreatedAt,
            task.UpdatedAt,
            task.LastActivityAt,
            replyCount,
            attachments);
    }

    private Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);

    private ActionResult InvalidField(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]>
            {
                [field] = [message],
            }));

    private ObjectResult NotFoundProblem(string title, string detail) =>
        NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = detail,
        });

    private ObjectResult BadRequestProblem(string detail) =>
        BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid operation",
            Detail = detail,
        });
}
