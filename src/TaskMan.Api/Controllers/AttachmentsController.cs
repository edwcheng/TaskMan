using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskMan.Api.Contracts;
using TaskMan.Api.Security;
using TaskMan.Domain.Attachments;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Infrastructure.Storage;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class AttachmentsController(
    AppDbContext db,
    IAttachmentStore attachments,
    ILogger<AttachmentsController> logger) : ControllerBase
{
    // Matches the Nginx client_max_body_size so the API, not the proxy, reports
    // the limit for in-process runs. Files over 25 MB are still rejected by the
    // store while the metadata row is being inserted.
    private const long MaxUploadBytes = 30 * 1024 * 1024;

    [HttpPost("tasks/{taskId:guid}/attachments")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<AttachmentResponse>> UploadToTask(
        Guid taskId,
        [FromForm] UploadAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        if (file is null)
        {
            return InvalidField("file", "A file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return PayloadTooLargeProblem();
        }

        if (!await db.Tasks.AnyAsync(x => x.Id == taskId, cancellationToken))
        {
            return NotFoundProblem("Task not found", "The requested task does not exist.");
        }

        var count = await db.Attachments
            .CountAsync(x => x.TaskId == taskId, cancellationToken);

        if (count >= AttachmentRules.MaxAttachmentsPerParent)
        {
            return BadRequestProblem(
                $"A task can have at most {AttachmentRules.MaxAttachmentsPerParent} attachments.");
        }

        return await SaveUploadAsync(taskId, null, file, cancellationToken);
    }

    [HttpPost("replies/{replyId:guid}/attachments")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<AttachmentResponse>> UploadToReply(
        Guid replyId,
        [FromForm] UploadAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        if (file is null)
        {
            return InvalidField("file", "A file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return PayloadTooLargeProblem();
        }

        if (!await db.Replies.AnyAsync(x => x.Id == replyId, cancellationToken))
        {
            return NotFoundProblem("Reply not found", "The requested reply does not exist.");
        }

        var count = await db.Attachments
            .CountAsync(x => x.ReplyId == replyId, cancellationToken);

        if (count >= AttachmentRules.MaxAttachmentsPerParent)
        {
            return BadRequestProblem(
                $"A reply can have at most {AttachmentRules.MaxAttachmentsPerParent} attachments.");
        }

        return await SaveUploadAsync(null, replyId, file, cancellationToken);
    }

    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] string? inline,
        CancellationToken cancellationToken)
    {
        // The plan specifies ?inline=1; bool binding would reject "1".
        var inlineRequested =
            string.Equals(inline, "1", StringComparison.Ordinal)
            || string.Equals(inline, "true", StringComparison.OrdinalIgnoreCase);

        var attachment = await db.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (attachment is null)
        {
            return NotFoundProblem(
                "Attachment not found",
                "The requested attachment does not exist.");
        }

        var contentType = attachment.ContentType;

        if (inlineRequested)
        {
            var inlineType = AttachmentRules.GetInlineContentType(
                attachment.FileName,
                attachment.ContentType);

            if (inlineType is null)
            {
                return BadRequestProblem(
                    "Only raster images (PNG, JPEG, GIF, WebP) may be served inline.");
            }

            contentType = inlineType;
        }

        var stream = attachments.OpenRead(id);

        if (stream is null)
        {
            logger.LogWarning(
                "Attachment file {AttachmentId} was not found on disk.",
                id);

            return NotFoundProblem(
                "Attachment file not found",
                "The stored file for this attachment is missing.");
        }

        // The filename is RFC 5987-encoded; the serializer cannot express this
        // reliably, so the header is written directly and FileDownloadName is
        // left unset to keep it untouched.
        HttpContext.Response.Headers["Content-Disposition"] =
            $"{(inlineRequested ? "inline" : "attachment")}; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
        HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        return new FileStreamResult(stream, contentType)
        {
            EnableRangeProcessing = true,
        };
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (attachment is null)
        {
            return NotFoundProblem(
                "Attachment not found",
                "The requested attachment does not exist.");
        }

        Guid parentAuthorId;

        if (attachment.TaskId is { } taskId)
        {
            parentAuthorId = await db.Tasks
                .AsNoTracking()
                .Where(x => x.Id == taskId)
                .Select(x => x.AuthorId)
                .SingleAsync(cancellationToken);
        }
        else
        {
            parentAuthorId = await db.Replies
                .AsNoTracking()
                .Where(x => x.Id == attachment.ReplyId!)
                .Select(x => x.AuthorId)
                .SingleAsync(cancellationToken);
        }

        if (!AttachmentPermissions.CanDelete(
                attachment,
                parentAuthorId,
                User.GetUserId(),
                User.IsInRole(nameof(UserRole.Admin))))
        {
            return Forbid();
        }

        // Delete the metadata row first, then the file. A failed file removal
        // leaves a harmless orphan.
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);

        attachments.Delete(attachment.Id);

        return NoContent();
    }

    private async Task<ActionResult<AttachmentResponse>> SaveUploadAsync(
        Guid? taskId,
        Guid? replyId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (AttachmentRules.ValidateFileName(file.FileName) is { } fileNameError)
        {
            return InvalidField("file", fileNameError);
        }

        if (AttachmentRules.ValidateContentType(file.ContentType) is { } contentTypeError)
        {
            return InvalidField("file", contentTypeError);
        }

        var attachment = new Attachment
        {
            TaskId = taskId,
            ReplyId = replyId,
            FileName = AttachmentRules.SanitizeFileName(file.FileName),
            ContentType = AttachmentRules.NormalizeContentType(file.ContentType),
            UploadedById = User.GetUserId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        long sizeBytes;

        try
        {
            using var stream = file.OpenReadStream();
            sizeBytes = await attachments.SaveAsync(
                stream,
                attachment.Id,
                AttachmentRules.MaxFileSizeBytes,
                cancellationToken);
        }
        catch (AttachmentTooLargeException)
        {
            return BadRequestProblem(
                $"Uploaded files must be at most {AttachmentRules.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        attachment.SizeBytes = sizeBytes;

        try
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The file is already on disk but has no metadata row; remove it and
            // let the caller see the failure.
            attachments.Delete(attachment.Id);
            throw;
        }

        return Created(
            $"/api/attachments/{attachment.Id}",
            await LoadAttachmentAsync(attachment.Id, cancellationToken));
    }

    private async Task<AttachmentResponse?> LoadAttachmentAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var uploader = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == attachment.UploadedById)
            .Select(x => new UserSummaryResponse(
                x.Id,
                x.UserName,
                x.DisplayName))
            .SingleAsync(cancellationToken);

        return new AttachmentResponse(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            uploader,
            attachment.CreatedAt);
    }

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

    private ObjectResult PayloadTooLargeProblem() =>
        StatusCode(
            StatusCodes.Status413PayloadTooLarge,
            new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Payload too large",
                Detail =
                    $"Uploaded requests must be at most {MaxUploadBytes / (1024 * 1024)} MB.",
            });
}
