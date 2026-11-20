using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Infrastructure.Persistence;

namespace TaskMan.Tests.Infrastructure;

[Collection(ApiCollection.Name)]
public sealed class DataFoundationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Health_reports_database_and_storage_ready()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload!.Status);
        Assert.Equal("ok", payload.Database);
        Assert.Equal("ok", payload.Storage);
    }

    [Fact]
    public async Task Startup_migrates_and_seeds_an_administrator()
    {
        fixture.Factory.CreateClient().Dispose();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var admin = await db.Users.SingleAsync(x => x.UserName == ApiFixture.AdminUserName);
        Assert.Equal("admin", admin.UserName);
        Assert.Equal("ADMIN", admin.NormalizedUserName);
        Assert.Equal("Test Administrator", admin.DisplayName);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
        Assert.NotEqual(Guid.Empty, admin.SecurityStamp);
        Assert.NotEqual(default, admin.CreatedAt);

        var hasher = new PasswordHasher<User>();
        var verification = hasher.VerifyHashedPassword(
            admin,
            admin.PasswordHash,
            ApiFixture.AdminPassword);

        Assert.NotEqual(PasswordVerificationResult.Failed, verification);
    }

    [Fact]
    public async Task Task_numbers_come_from_the_database_sequence()
    {
        fixture.Factory.CreateClient().Dispose();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await db.Users.SingleAsync(x => x.UserName == ApiFixture.AdminUserName);

        var forum = new Forum
        {
            Name = "General",
            Slug = "general",
            SortOrder = 0,
        };

        db.Forums.Add(forum);
        await db.SaveChangesAsync();

        var first = new TaskItem
        {
            ForumId = forum.Id,
            Title = "First task",
            Body = "Body",
            AuthorId = admin.Id,
        };

        var second = new TaskItem
        {
            ForumId = forum.Id,
            Title = "Second task",
            Body = "Body",
            AuthorId = admin.Id,
        };

        db.Tasks.Add(first);
        await db.SaveChangesAsync();

        db.Tasks.Add(second);
        await db.SaveChangesAsync();

        Assert.True(first.Number > 0);
        Assert.Equal(first.Number + 1, second.Number);

        var persisted = await db.Tasks
            .Where(x => x.Id == first.Id || x.Id == second.Id)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Id, x.Number })
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                new { first.Id, first.Number },
                new { second.Id, second.Number },
            },
            persisted);
    }

    private sealed record HealthPayload(
        string Status,
        DateTimeOffset Utc,
        string Database,
        string Storage);
}
