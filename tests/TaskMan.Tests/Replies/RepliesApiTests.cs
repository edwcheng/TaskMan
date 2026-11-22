using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Replies;

[Collection(ApiCollection.Name)]
public sealed class RepliesApiTests(ApiFixture fixture)
{
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Create_requires_authentication()
    {
        using var anonymous = ApiTestClient.Create(fixture.Factory);

        var response = await anonymous.PostAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}/replies",
            new { body = "Hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_validates_task_and_body()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply create {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var missingTask = await admin.PostAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}/replies",
            new { body = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, missingTask.StatusCode);

        var blankBody = await admin.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/replies",
            new { body = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, blankBody.StatusCode);

        var longBody = await admin.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/replies",
            new { body = new string('a', 100_001) });

        Assert.Equal(HttpStatusCode.BadRequest, longBody.StatusCode);
    }

    [Fact]
    public async Task Create_adds_reply_touches_activity_and_counts_it()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply activity {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var before = await admin.GetFromJsonAsync<TaskDetailPayload>($"/api/tasks/{task.Id}");
        Assert.NotNull(before);
        Assert.Equal(0, before!.ReplyCount);

        var reply = await ApiTestClient.CreateReplyAsync(admin, task.Id, "First reply");

        Assert.Equal("First reply", reply.Body);
        Assert.Equal("admin", reply.Author.UserName);
        Assert.Empty(reply.Attachments);

        var after = await admin.GetFromJsonAsync<TaskDetailPayload>($"/api/tasks/{task.Id}");
        Assert.NotNull(after);
        Assert.Equal(1, after!.ReplyCount);
        Assert.True(after.LastActivityAt >= before!.LastActivityAt);
    }

    [Fact]
    public async Task Create_is_allowed_on_tasks_in_archived_forums()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();

        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply archived {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var archive = await admin.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = forum.Name,
                slug = forum.Slug,
                description = forum.Description,
                sortOrder = forum.SortOrder,
                isArchived = true,
            });

        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var reply = await ApiTestClient.CreateReplyAsync(
            admin,
            task.Id,
            "Archived discussion");

        Assert.Equal("Archived discussion", reply.Body);
    }

    [Fact]
    public async Task List_is_chronological_and_paginated_at_fifty_per_page()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply pages {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var first = await ApiTestClient.CreateReplyAsync(admin, task.Id, "Reply 1");

        for (var i = 2; i <= 51; i++)
        {
            await ApiTestClient.CreateReplyAsync(admin, task.Id, $"Reply {i}");
        }

        var page1 = await ApiTestClient.GetReplyPageAsync(admin, task.Id, page: 1);

        Assert.Equal(51, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(50, page1.PageSize);
        Assert.Equal(50, page1.Items.Count);
        Assert.Equal(first.Id, page1.Items[0].Id);

        var page2 = await ApiTestClient.GetReplyPageAsync(admin, task.Id, page: 2);

        Assert.Single(page2.Items);
        Assert.Equal("Reply 51", page2.Items[0].Body);

        for (var i = 0; i + 1 < page1.Items.Count; i++)
        {
            Assert.True(page1.Items[i].CreatedAt <= page1.Items[i + 1].CreatedAt);
        }
    }

    [Fact]
    public async Task List_requires_the_task_to_exist()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);

        var response = await admin.GetAsync($"/api/tasks/{Guid.NewGuid()}/replies");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_is_restricted_and_edits_the_body()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply update {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var strangerName = ApiTestClient.UniqueUserName("stranger");
        await ApiTestClient.CreateUserAsync(admin, strangerName);
        using var stranger = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            strangerName,
            ApiTestClient.UserPassword);

        var reply = await ApiTestClient.CreateReplyAsync(admin, task.Id, "Original");

        var forbidden = await stranger.PutAsJsonAsync(
            $"/api/replies/{reply.Id}",
            new { body = "Tampered" });

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var updated = await admin.PutAsJsonAsync(
            $"/api/replies/{reply.Id}",
            new { body = "Edited body" });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var payload = await updated.Content.ReadFromJsonAsync<ReplyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Edited body", payload!.Body);

        var page = await ApiTestClient.GetReplyPageAsync(admin, task.Id);
        Assert.Equal("Edited body", page.Items.Single().Body);

        var blank = await admin.PutAsJsonAsync(
            $"/api/replies/{reply.Id}",
            new { body = "" });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var missing = await admin.PutAsJsonAsync(
            $"/api/replies/{Guid.NewGuid()}",
            new { body = "Nope" });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_is_restricted_and_removes_attachment_files()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Reply delete {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        var strangerName = ApiTestClient.UniqueUserName("stranger");
        await ApiTestClient.CreateUserAsync(admin, strangerName);
        using var stranger = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            strangerName,
            ApiTestClient.UserPassword);

        var reply = await ApiTestClient.CreateReplyAsync(admin, task.Id, "With attachment");

        var content = new byte[] { 1, 2, 3 };
        var attachment = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/replies/{reply.Id}/attachments",
            "evidence.bin",
            content,
            "application/octet-stream");

        var forbidden = await stranger.DeleteAsync($"/api/replies/{reply.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await admin.DeleteAsync($"/api/replies/{reply.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await ApiTestClient.GetReplyPageAsync(admin, task.Id);
        Assert.Empty(page.Items);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Replies.CountAsync(x => x.TaskId == task.Id));
        Assert.Equal(0, await db.Attachments.CountAsync(x => x.ReplyId == reply.Id));

        var name = attachment.Id.ToString("N");
        Assert.False(File.Exists(Path.Combine(fixture.AttachmentsPath, name[..2], name)));
    }

    [Fact]
    public async Task Delete_missing_reply_is_not_found()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);

        var response = await admin.DeleteAsync($"/api/replies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
