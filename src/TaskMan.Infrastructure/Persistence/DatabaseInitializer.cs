using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Domain.Users;

namespace TaskMan.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    AppDbContext db,
    IConfiguration configuration,
    IPasswordHasher<User> passwordHasher,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger)
{
    private const long AdvisoryLockKey = 0x5441534B4D414E31;
    private const int MaxMigrationAttempts = 10;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock({0})",
                new object[] { AdvisoryLockKey },
                cancellationToken);

            try
            {
                await MigrateWithRetryAsync(cancellationToken);
                await SeedAdminAsync(cancellationToken);
            }
            finally
            {
                await ReleaseLockAsync(cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task MigrateWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied.");
                return;
            }
            catch (Exception ex) when (attempt < MaxMigrationAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Database migration attempt {Attempt} of {MaxAttempts} failed; retrying in {DelaySeconds}s.",
                    attempt,
                    MaxMigrationAttempts,
                    RetryDelay.TotalSeconds);

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var userName = (configuration["ADMIN_USERNAME"] ?? "admin").Trim();
        var displayName = (configuration["ADMIN_DISPLAY_NAME"] ?? "Administrator").Trim();
        var password = configuration["ADMIN_PASSWORD"];

        ValidateAdminSettings(userName, displayName, password);

        var admin = new User
        {
            UserName = userName,
            NormalizedUserName = UserRules.NormalizeUserName(userName),
            DisplayName = displayName,
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        admin.PasswordHash = passwordHasher.HashPassword(admin, password!);

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded administrator {UserName} in the {Environment} environment.",
            admin.UserName,
            environment.EnvironmentName);
    }

    private async Task ReleaseLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock({0})",
                new object[] { AdvisoryLockKey },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Closing the connection releases the session lock; never mask the original failure.
            logger.LogWarning(ex, "Failed to release the database advisory lock explicitly.");
        }
    }

    private static void ValidateAdminSettings(string userName, string displayName, string? password)
    {
        if (UserRules.ValidateUserName(userName) is { } userNameError)
        {
            throw new InvalidOperationException($"ADMIN_USERNAME is invalid: {userNameError}");
        }

        if (UserRules.ValidateDisplayName(displayName) is { } displayNameError)
        {
            throw new InvalidOperationException($"ADMIN_DISPLAY_NAME is invalid: {displayNameError}");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "ADMIN_PASSWORD is required to seed the first administrator. Set it to at least 8 characters.");
        }

        if (UserRules.ValidatePassword(password) is { } passwordError)
        {
            throw new InvalidOperationException($"ADMIN_PASSWORD is invalid: {passwordError}");
        }
    }
}
