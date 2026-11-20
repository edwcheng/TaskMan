using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace TaskMan.Tests.Infrastructure;

public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "test-admin-password";

    private PostgreSqlContainer? _container;

    public string AttachmentsPath { get; } =
        Path.Combine(Path.GetTempPath(), "taskman-tests", Guid.NewGuid().ToString("N"));

    public string KeysPath => Path.Combine(AttachmentsPath, "keys");

    public string ConnectionString { get; private set; } = string.Empty;

    public TaskManApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        Directory.CreateDirectory(AttachmentsPath);

        Factory = new TaskManApiFactory(ConnectionString, AttachmentsPath);
    }

    public TaskManApiFactory CreateFactory(
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(ConnectionString, AttachmentsPath, settings);

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        try
        {
            Directory.Delete(AttachmentsPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // The test run never created the temporary directory.
        }
    }
}

public sealed class TaskManApiFactory(
    string connectionString,
    string attachmentsPath,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("ADMIN_USERNAME", ApiFixture.AdminUserName);
        builder.UseSetting("ADMIN_PASSWORD", ApiFixture.AdminPassword);
        builder.UseSetting("ADMIN_DISPLAY_NAME", "Test Administrator");
        builder.UseSetting("Storage:AttachmentsPath", attachmentsPath);
        builder.UseSetting("Storage:KeysPath", Path.Combine(attachmentsPath, "keys"));
        builder.UseSetting("Auth:LoginPermitLimit", "1000");

        if (settings is null)
        {
            return;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
