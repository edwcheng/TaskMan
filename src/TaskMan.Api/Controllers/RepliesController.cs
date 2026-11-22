using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskMan.Api.Contracts;
using TaskMan.Api.Security;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Replies;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Infrastructure.Storage;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api/replies")]
[Authorize]
public sealed class RepliesController(
    AppDbContext db,
    IAttachmentStore attachments) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet("/api/tasks/{taskId:guid}/replies")]
    public async Task<ActionResult<PagedResponse<ReplyResponse>>> List(
        Guid taskId,
        [FromQuery] ReplyListQuery query,
        CancellationToken cancellationToken)
    {
        if (!await db.Tasks.AnyAsync(x => x.Id == taskId, cancellationToken))
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaxPageSize);

        var replies = db.Replies.AsNoTracking().Where(x => x.TaskId == taskId);

        var totalCount = await replies.CountAsync(cancellationToken);

        var rows = await (
                from reply in replies
                join author in db.Users.AsNoTracking() on reply.AuthorId equals author.Id
                select new
                {
                    reply.Id,
                    reply.Body,
                    reply.CreatedAt,
                    reply.UpdatedAt,
                    AuthorId = author.Id,
                    AuthorUserName = author.UserName,
                    AuthorDisplayName = author.DisplayName,
                })
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // A second query loads the page's attachment metadata; the nested
        // collection projection is not translatable in a single query.
        var replyIds = rows.Select(x => x.Id).ToList();

        List<AttachmentRow> attachmentRows = replyIds.Count > 0
            ? await db.Attachments
                .AsNoTracking()
                .Where(x => x.ReplyId != null && replyIds.Contains(x.ReplyId!.Value))
                .OrderBy(x => x.CreatedAt)
                .Select(x => new AttachmentRow(
                    x.Id,
                    x.ReplyId!.Value,
                    x.FileName,
                    x.ContentType,
                    x.SizeBytes,
                    x.UploadedById,
                    x.CreatedAt,
                    db.Users
                        .Where(u => u.Id == x.UploadedById)
                        .Select(u => u.UserName)
                        .First(),
                    db.Users
                        .Where(u => u.Id == x.UploadedById)
                        .Select(u => u.DisplayName)
                        .First()))
                .ToListAsync(cancellationToken)
            : [];

        var attachmentsByReply = attachmentRows
            .GroupBy(x => x.ReplyId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var items = rows
            .Select(row =>
            {
                attachmentsByReply.TryGetValue(row.Id, out var attachments);

                return new ReplyResponse(
                    row.Id,
                    row.Body,
                    new UserSummaryResponse(
                        row.AuthorId,
                        row.AuthorUserName,
                        row.AuthorDisplayName),
                    row.CreatedAt,
                    row.UpdatedAt,
                    (attachments ?? [])
                        .Select(a => new AttachmentResponse(
                            a.Id,
                            a.FileName,
                            a.ContentType,
                            a.SizeBytes,
                            new UserSummaryResponse(
                                a.UploadedById,
                                a.UploadedByUserName,
                                a.UploadedByDisplayName),
                            a.CreatedAt))
                        .ToList());
            })
            .ToList();

        return Ok(new PagedResponse<ReplyResponse>(
            items,
            page,
            pageSize,
            totalCount));
    }

    [HttpPost("/api/tasks/{taskId:guid}/replies")]
    public async Task<ActionResult<ReplyResponse>> Create(
        Guid taskId,
        [FromBody] CreateReplyRequest request,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == taskId, cancellationToken);

        if (task is null)
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        if (ReplyRules.ValidateBody(request.Body) is { } bodyError)
        {
            return InvalidField("body", bodyError);
        }

        var now = DateTimeOffset.UtcNow;

        var reply = new Reply
        {
            TaskId = taskId,
            AuthorId = User.GetUserId(),
            Body = request.Body!,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Replies.Add(reply);

        // New replies are task activity (see the LastActivityAt definition).
        task.LastActivityAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return Created(
            $"/api/tasks/{taskId}/replies",
            await LoadReplyAsync(reply.Id, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReplyResponse>> Update(
        Guid id,
        [FromBody] UpdateReplyRequest request,
        CancellationToken cancellationToken)
    {
        var reply = await db.Replies.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
        {
            return NotFoundProblem("Reply not found", "The requested reply does not exist.");
        }

        if (!ReplyPermissions.CanEdit(
                reply,
                User.GetUserId(),
                User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        if (ReplyRules.ValidateBody(request.Body) is { } bodyError)
        {
            return InvalidField("body", bodyError);
        }

        reply.Body = request.Body!;
        reply.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(await LoadReplyAsync(reply.Id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var reply = await db.Replies.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
        {
            return NotFoundProblem("Reply not found", "The requested reply does not exist.");
        }

        if (!ReplyPermissions.CanEdit(
                reply,
                User.GetUserId(),
                User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        // Delete the rows in one transaction first, then the files on disk.
        var replyAttachments = await db.Attachments
            .Where(x => x.ReplyId == reply.Id)
            .ToListAsync(cancellationToken);

        foreach (var attachment in replyAttachments)
        {
            db.Attachments.Remove(attachment);
        }

        db.Replies.Remove(reply);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var attachment in replyAttachments)
        {
            attachments.Delete(attachment.Id);
        }

        return NoContent();
    }

    private async Task<ReplyResponse?> LoadReplyAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var reply = await db.Replies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
        {
            return null;
        }

        var author = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == reply.AuthorId)
            .Select(x => new UserSummaryResponse(
                x.Id,
                x.UserName,
                x.DisplayName))
            .SingleAsync(cancellationToken);

        var attachmentRows = await db.Attachments
            .AsNoTracking()
            .Where(x => x.ReplyId == reply.Id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new AttachmentRow(
                x.Id,
                reply.Id,
                x.FileName,
                x.ContentType,
                x.SizeBytes,
                x.UploadedById,
                x.CreatedAt,
                db.Users
                    .Where(u => u.Id == x.UploadedById)
                    .Select(u => u.UserName)
                    .First(),
                db.Users
                    .Where(u => u.Id == x.UploadedById)
                    .Select(u => u.DisplayName)
                    .First()))
            .ToListAsync(cancellationToken);

        return new ReplyResponse(
            reply.Id,
            reply.Body,
            author,
            reply.CreatedAt,
            reply.UpdatedAt,
            attachmentRows
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
                .ToList());
    }

    private sealed record AttachmentRow(
        Guid Id,
        Guid ReplyId,
        string FileName,
        string ContentType,
        long SizeBytes,
        Guid UploadedById,
        DateTimeOffset CreatedAt,
        string UploadedByUserName,
        string UploadedByDisplayName);

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
}
