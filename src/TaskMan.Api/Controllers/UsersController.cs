using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskMan.Api.Contracts;
using TaskMan.Api.Mapping;
using TaskMan.Api.Security;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Users;
using TaskMan.Infrastructure.Auth;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<PagedResponse<UserResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] UserRole? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var normalized = UserRules.NormalizeUserName(term);

            users = users.Where(x =>
                x.NormalizedUserName.Contains(normalized)
                || EF.Functions.ILike(x.DisplayName, $"%{term}%"));
        }

        if (role is not null)
        {
            users = users.Where(x => x.Role == role);
        }

        if (isActive is not null)
        {
            users = users.Where(x => x.IsActive == isActive);
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var items = await users
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<UserResponse>(
            items.Select(x => x.ToResponse()).ToList(),
            page,
            pageSize,
            totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<UserResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return user is null
            ? NotFoundProblem()
            : Ok(user.ToResponse());
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<UserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (UserRules.ValidateUserName(request.UserName) is { } userNameError)
        {
            return InvalidField("userName", userNameError);
        }

        if (UserRules.ValidateDisplayName(request.DisplayName) is { } displayNameError)
        {
            return InvalidField("displayName", displayNameError);
        }

        if (UserRules.ValidatePassword(request.Password) is { } passwordError)
        {
            return InvalidField("password", passwordError);
        }

        if (!Enum.IsDefined(request.Role))
        {
            return InvalidField("role", "Role is invalid.");
        }

        var userName = request.UserName!.Trim();
        var normalizedUserName = UserRules.NormalizeUserName(userName);

        if (await db.Users.AnyAsync(
                x => x.NormalizedUserName == normalizedUserName,
                cancellationToken))
        {
            return ConflictProblem("A user with this username already exists.");
        }

        var user = new User
        {
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            DisplayName = request.DisplayName!.Trim(),
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ConflictProblem("A user with this username already exists.");
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = user.Id },
            user.ToResponse());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (UserRules.ValidateDisplayName(request.DisplayName) is { } displayNameError)
        {
            return InvalidField("displayName", displayNameError);
        }

        if (request.Role is null || request.IsActive is null)
        {
            return InvalidField("role", "Role and active flag are required.");
        }

        if (!Enum.IsDefined(request.Role.Value))
        {
            return InvalidField("role", "Role is invalid.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFoundProblem();
        }

        var newRole = request.Role.Value;
        var newIsActive = request.IsActive.Value;

        if (user.Id == User.GetUserId()
            && (newRole != UserRole.Admin || !newIsActive))
        {
            return BadRequestProblem(
                "You cannot demote or deactivate your own account.");
        }

        if (await IsLastActiveAdminAsync(user, newRole, newIsActive, cancellationToken))
        {
            return BadRequestProblem(
                "At least one active administrator must remain.");
        }

        var roleChanged = user.Role != newRole;
        var activeChanged = user.IsActive != newIsActive;

        user.DisplayName = request.DisplayName!.Trim();
        user.Role = newRole;
        user.IsActive = newIsActive;

        if (roleChanged || activeChanged)
        {
            user.SecurityStamp = Guid.NewGuid();
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(user.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFoundProblem();
        }

        if (user.Id == User.GetUserId())
        {
            return BadRequestProblem("You cannot deactivate your own account.");
        }

        if (await IsLastActiveAdminAsync(user, UserRole.User, false, cancellationToken))
        {
            return BadRequestProblem("At least one active administrator must remain.");
        }

        if (user.IsActive)
        {
            user.IsActive = false;
            user.SecurityStamp = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (UserRules.ValidatePassword(request.NewPassword) is { } passwordError)
        {
            return InvalidField("newPassword", passwordError);
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFoundProblem();
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync(cancellationToken);

        if (user.Id == User.GetUserId())
        {
            var sessionStartedAt = SessionClaims.GetSessionStarted(
                User,
                DateTimeOffset.UtcNow);

            await AuthSession.SignInAsync(HttpContext, user, sessionStartedAt);
        }

        return NoContent();
    }

    [HttpGet("assignable")]
    public async Task<ActionResult<IReadOnlyList<AssignableUserResponse>>> Assignable(
        CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new AssignableUserResponse(
                x.Id,
                x.UserName,
                x.DisplayName))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    private async Task<bool> IsLastActiveAdminAsync(
        User user,
        UserRole newRole,
        bool newIsActive,
        CancellationToken cancellationToken)
    {
        if (user.Role != UserRole.Admin
            || !user.IsActive
            || (newRole == UserRole.Admin && newIsActive))
        {
            return false;
        }

        return !await db.Users.AnyAsync(
            x => x.Id != user.Id && x.Role == UserRole.Admin && x.IsActive,
            cancellationToken);
    }

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
            Title = "User not found",
            Detail = "The requested user does not exist.",
        });

    private ObjectResult ConflictProblem(string detail) =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
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
