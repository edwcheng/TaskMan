using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaskMan.Api.Contracts;
using TaskMan.Api.Mapping;
using TaskMan.Api.Security;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Users;
using TaskMan.Infrastructure.Auth;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<UserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrEmpty(request.Password))
        {
            return UnauthorizedProblem();
        }

        var normalizedUserName = UserRules.NormalizeUserName(request.UserName);

        var user = await db.Users.SingleOrDefaultAsync(
            x => x.NormalizedUserName == normalizedUserName,
            cancellationToken);

        var verification = user is null
            ? PasswordVerificationResult.Failed
            : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (user is null
            || !user.IsActive
            || verification == PasswordVerificationResult.Failed)
        {
            return UnauthorizedProblem();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        var now = DateTimeOffset.UtcNow;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await AuthSession.SignInAsync(HttpContext, user, now);

        return Ok(user.ToResponse());
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await AuthSession.SignOutAsync(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            await AuthSession.SignOutAsync(HttpContext);
            return UnauthorizedProblem();
        }

        return Ok(user.ToResponse());
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            return InvalidField("currentPassword", "Current password is required.");
        }

        if (UserRules.ValidatePassword(request.NewPassword) is { } passwordError)
        {
            return InvalidField("newPassword", passwordError);
        }

        var userId = User.GetUserId();

        var user = await db.Users.SingleOrDefaultAsync(
            x => x.Id == userId,
            cancellationToken);

        if (user is null)
        {
            await AuthSession.SignOutAsync(HttpContext);
            return UnauthorizedProblem();
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.CurrentPassword);

        if (verification == PasswordVerificationResult.Failed)
        {
            return InvalidField("currentPassword", "Current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync(cancellationToken);

        var sessionStartedAt = SessionClaims.GetSessionStarted(
            User,
            DateTimeOffset.UtcNow);

        await AuthSession.SignInAsync(HttpContext, user, sessionStartedAt);

        return NoContent();
    }

    private UnauthorizedObjectResult UnauthorizedProblem() =>
        Unauthorized(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Invalid credentials",
            Detail = "Invalid username or password.",
        });

    private ActionResult InvalidField(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]>
            {
                [field] = [message],
            }));
}
