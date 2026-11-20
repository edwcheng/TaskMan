using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskMan.Api.Contracts;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Forums;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api/forums")]
[Authorize]
public sealed class ForumsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ForumResponse>>> List(
        [FromQuery] bool includeArchived = true,
        CancellationToken cancellationToken = default)
    {
        var forums = db.Forums.AsNoTracking();

        if (!includeArchived)
        {
            forums = forums.Where(x => !x.IsArchived);
        }

        var response = await ProjectForums(
                forums.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            .ToListAsync(cancellationToken);

        return Ok(response);
    }

    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<ForumResponse>> Get(
        string idOrSlug,
        CancellationToken cancellationToken)
    {
        var forums = db.Forums.AsNoTracking();

        if (Guid.TryParse(idOrSlug, out var id))
        {
            forums = forums.Where(x => x.Id == id);
        }
        else
        {
            var slug = ForumRules.NormalizeSlug(idOrSlug);
            forums = forums.Where(x => x.Slug == slug);
        }

        var forum = await ProjectForums(forums).SingleOrDefaultAsync(cancellationToken);

        return forum is null ? NotFoundProblem() : Ok(forum);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ForumResponse>> Create(
        CreateForumRequest request,
        CancellationToken cancellationToken)
    {
        if (ForumRules.ValidateName(request.Name) is { } nameError)
        {
            return InvalidField("name", nameError);
        }

        if (ForumRules.ValidateDescription(request.Description) is { } descriptionError)
        {
            return InvalidField("description", descriptionError);
        }

        var name = request.Name!.Trim();
        var explicitSlug = !string.IsNullOrWhiteSpace(request.Slug);
        string slug;

        if (explicitSlug)
        {
            slug = ForumRules.NormalizeSlug(request.Slug!);

            if (ForumRules.ValidateSlug(slug) is { } slugError)
            {
                return InvalidField("slug", slugError);
            }

            if (await db.Forums.AnyAsync(x => x.Slug == slug, cancellationToken))
            {
                return ConflictProblem("A forum with this slug already exists.");
            }
        }
        else
        {
            slug = await EnsureUniqueSlugAsync(
                ForumRules.Slugify(name),
                excludedId: null,
                cancellationToken);
        }

        var forum = new Forum
        {
            Name = name,
            Slug = slug,
            Description = NormalizeDescription(request.Description),
            SortOrder = request.SortOrder ?? 0,
            IsArchived = request.IsArchived ?? false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Forums.Add(forum);
        await db.SaveChangesAsync(cancellationToken);

        var response = await LoadForumAsync(forum.Id, cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { idOrSlug = forum.Id },
            response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ForumResponse>> Update(
        Guid id,
        UpdateForumRequest request,
        CancellationToken cancellationToken)
    {
        if (ForumRules.ValidateName(request.Name) is { } nameError)
        {
            return InvalidField("name", nameError);
        }

        if (ForumRules.ValidateDescription(request.Description) is { } descriptionError)
        {
            return InvalidField("description", descriptionError);
        }

        if (request.SortOrder is null || request.IsArchived is null)
        {
            return InvalidField(
                "sortOrder",
                "Sort order and archive flag are required.");
        }

        var forum = await db.Forums.SingleOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);

        if (forum is null)
        {
            return NotFoundProblem();
        }

        var name = request.Name!.Trim();
        var explicitSlug = !string.IsNullOrWhiteSpace(request.Slug);
        string slug;

        if (explicitSlug)
        {
            slug = ForumRules.NormalizeSlug(request.Slug!);

            if (ForumRules.ValidateSlug(slug) is { } slugError)
            {
                return InvalidField("slug", slugError);
            }

            if (await db.Forums.AnyAsync(
                    x => x.Slug == slug && x.Id != forum.Id,
                    cancellationToken))
            {
                return ConflictProblem("A forum with this slug already exists.");
            }
        }
        else
        {
            slug = await EnsureUniqueSlugAsync(
                ForumRules.Slugify(name),
                forum.Id,
                cancellationToken);
        }

        forum.Name = name;
        forum.Slug = slug;
        forum.Description = NormalizeDescription(request.Description);
        forum.SortOrder = request.SortOrder.Value;
        forum.IsArchived = request.IsArchived.Value;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(await LoadForumAsync(forum.Id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var forum = await db.Forums.SingleOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);

        if (forum is null)
        {
            return NotFoundProblem();
        }

        if (await db.Tasks.AnyAsync(x => x.ForumId == id, cancellationToken))
        {
            return ConflictProblem(
                "A forum can only be deleted when it contains no tasks.");
        }

        db.Forums.Remove(forum);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private IQueryable<ForumResponse> ProjectForums(IQueryable<Forum> forums) =>
        forums.Select(forum => new ForumResponse(
            forum.Id,
            forum.Name,
            forum.Slug,
            forum.Description,
            forum.SortOrder,
            forum.IsArchived,
            forum.CreatedAt,
            db.Tasks.Count(x => x.ForumId == forum.Id),
            db.Tasks.Count(
                x => x.ForumId == forum.Id
                    && x.Status != TaskItemStatus.Completed
                    && x.Status != TaskItemStatus.Closed),
            db.Tasks
                .Where(x => x.ForumId == forum.Id)
                .Max(x => (DateTimeOffset?)x.LastActivityAt)));

    private async Task<ForumResponse> LoadForumAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await ProjectForums(db.Forums.AsNoTracking().Where(x => x.Id == id))
            .SingleAsync(cancellationToken);

    private async Task<string> EnsureUniqueSlugAsync(
        string baseSlug,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var slug = baseSlug;
        var suffix = 2;

        while (await db.Forums.AnyAsync(
                   x => x.Slug == slug
                       && (excludedId == null || x.Id != excludedId),
                   cancellationToken))
        {
            var suffixText = $"-{suffix++}";
            var maxBaseLength = ForumRules.SlugMaxLength - suffixText.Length;
            var truncated = baseSlug.Length > maxBaseLength
                ? baseSlug[..maxBaseLength].TrimEnd('-')
                : baseSlug;

            slug = truncated + suffixText;
        }

        return slug;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private ActionResult InvalidField(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]>
            {
                [field] = [message],
            }));

    private ObjectResult NotFoundProblem() =>
        NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Forum not found",
            Detail = "The requested forum does not exist.",
        });

    private ObjectResult ConflictProblem(string detail) =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = detail,
        });
}
