using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskMan.Domain.Entities;
using TaskMan.Infrastructure.Auth;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Infrastructure.Storage;

namespace TaskMan.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        StoragePaths storagePaths)
    {
        var connectionString =
            configuration.GetConnectionString("Database")
            ?? configuration["Database:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The database connection string is not configured. Set Database__ConnectionString.");
        }

        storagePaths.EnsureWritable();

        services.AddSingleton(storagePaths);
        services.AddSingleton<IAttachmentStore>(
            sp => new DiskAttachmentStore(
                storagePaths.AttachmentsPath,
                sp.GetRequiredService<ILogger<DiskAttachmentStore>>()));
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<DatabaseInitializer>();

        services.AddTaskManAuthentication(configuration);
        services.AddTaskManDataProtection(storagePaths);

        return services;
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }
}
