using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Infrastructure.Auth;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddTaskManAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        authOptions.Validate();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddSingleton(authOptions);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = AuthDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                options.Cookie.SecurePolicy = authOptions.CookieSecure
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;

                options.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionIdleHours);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        await ValidatePrincipalAsync(
                            context,
                            authOptions,
                            context.HttpContext.RequestAborted);
                    },
                    OnRedirectToLogin = context => WriteProblemAsync(
                        context.Response,
                        StatusCodes.Status401Unauthorized,
                        "Unauthorized",
                        "Authentication is required."),
                    OnRedirectToAccessDenied = context => WriteProblemAsync(
                        context.Response,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "You do not have permission to perform this action."),
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddTaskManDataProtection(
        this IServiceCollection services,
        Storage.StoragePaths storagePaths)
    {
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(storagePaths.KeysPath))
            .SetApplicationName("TaskMan");

        return services;
    }

    private static async Task ValidatePrincipalAsync(
        CookieValidatePrincipalContext context,
        AuthOptions authOptions,
        CancellationToken cancellationToken)
    {
        var principal = context.Principal;
        var userIdValue = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (principal is null || !Guid.TryParse(userIdValue, out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        var failure = SessionValidator.Validate(
            user,
            principal,
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(authOptions.SessionAbsoluteDays));

        if (failure is null)
        {
            return;
        }

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("TaskMan.Auth.Session");

        logger.LogInformation(
            "Rejected session for user {UserId}: {Reason}",
            userId,
            failure);

        await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static Task WriteProblemAsync(
        HttpResponse response,
        int statusCode,
        string title,
        string detail)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";

        return response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: response.HttpContext.RequestAborted);
    }
}
