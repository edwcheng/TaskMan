using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskMan.Domain.Entities;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Tasks;

[Collection(ApiCollection.Name)]
public sealed class TasksApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task Create_assigns_numbers_defaults_and_returns_detail()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Tasks {suffix}");

        var first = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"First {suffix}",
            body: "**Markdown** body");

        var second = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Second {suffix}");

        Assert.Equal(first.Number + 1, second.Number);
        Assert.Equal("Open", first.Status);
        Assert.Equal("Normal", first.Priority);
        Assert.Equal("**Markdown** body", first.Body);
        Assert.Equal(forum.Id, first.Forum.Id);
        Assert.Equal("admin", first.Author.UserName);
        Assert.Null(first.Assignee);
        Assert.Empty(first.Attachments);
        Assert.Equal(0, first.ReplyCount);

        var detail = await admin.GetFromJsonAsync<TaskDetailPayload>(
            $"/api/tasks/{first.Id}");
        Assert.NotNull(detail);
        Assert.Equal(first.Number, detail!.Number);
        Assert.Equal("First " + suffix, detail.Title);
        Assert.Equal("Open", detail.Status);
    }

    [Fact]
    public async Task Create_validates_fields_and_assignee()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Validate {suffix}");
        var archived = await ApiTestClient.CreateForumAsync(
            admin,
            $"Archived tasks {suffix}",
            isArchived: true);

        var assigneeName = ApiTestClient.UniqueUserName("assignee");
        var assignee = await ApiTestClient.CreateUserAsync(admin, assigneeName);

        var inactiveName = ApiTestClient.UniqueUserName("inactive");
        var inactive = await ApiTestClient.CreateUserAsync(admin, inactiveName);
        await admin.DeleteAsync($"/api/users/{inactive.Id}");

        var missingForum = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = Guid.NewGuid(),
                title = "Missing forum",
                body = "Body",
                status = (string?)null,
                priority = (string?)null,
                tag = (string?)null,
                dueDate = (DateOnly?)null,
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.NotFound, missingForum.StatusCode);

        var archivedForum = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = archived.Id,
                title = "Archived forum",
                body = "Body",
                status = (string?)null,
                priority = (string?)null,
                tag = (string?)null,
                dueDate = (DateOnly?)null,
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, archivedForum.StatusCode);

        var inactiveAssignee = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = forum.Id,
                title = "Inactive assignee",
                body = "Body",
                status = (string?)null,
                priority = (string?)null,
                tag = (string?)null,
                dueDate = (DateOnly?)null,
                assigneeId = inactive.Id,
            });

        Assert.Equal(HttpStatusCode.BadRequest, inactiveAssignee.StatusCode);

        var blankTitle = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = forum.Id,
                title = " ",
                body = "Body",
                status = (string?)null,
                priority = (string?)null,
                tag = (string?)null,
                dueDate = (DateOnly?)null,
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, blankTitle.StatusCode);

        var missingBody = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = forum.Id,
                title = "Missing body",
                body = (string?)null,
                status = (string?)null,
                priority = (string?)null,
                tag = (string?)null,
                dueDate = (DateOnly?)null,
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);

        var longTag = await admin.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId = forum.Id,
                title = "Long tag",
                body = "Body",
                status = (string?)null,
                priority = (string?)null,
                tag = new string('a', 51),
                dueDate = (DateOnly?)null,
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, longTag.StatusCode);

        var created = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Assigned {suffix}",
            status: "InProgress",
            priority: "High",
            tag: "release",
            dueDate: new DateOnly(2026, 12, 1),
            assigneeId: assignee.Id);

        Assert.Equal(assignee.Id, created.Assignee!.Id);
        Assert.Equal(assignee.UserName, created.Assignee.UserName);
        Assert.Equal("InProgress", created.Status);
        Assert.Equal("High", created.Priority);
        Assert.Equal("release", created.Tag);
        Assert.Equal(new DateOnly(2026, 12, 1), created.DueDate);
    }

    [Fact]
    public async Task List_filters_search_sorts_and_paginates()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var token = $"token{suffix}";
        var forum = await ApiTestClient.CreateForumAsync(admin, $"List {suffix}");

        var assigneeName = ApiTestClient.UniqueUserName("listuser");
        var assignee = await ApiTestClient.CreateUserAsync(admin, assigneeName);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var high = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Alpha {token}",
            body: "First body",
            status: "Open",
            priority: "High",
            tag: "release",
            dueDate: today.AddDays(-2),
            assigneeId: assignee.Id);

        var low = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Beta {token}",
            body: "Second body",
            status: "InProgress",
            priority: "Low",
            tag: "other",
            dueDate: today.AddDays(2));

        var urgent = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            "Gamma without token in title",
            body: $"Body mentions {token}",
            status: "Completed",
            priority: "Urgent",
            tag: "release",
            dueDate: today.AddDays(-5),
            assigneeId: assignee.Id);

        var normal = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Delta {token}",
            body: "Fourth body",
            status: "OnHold",
            priority: "Normal",
            tag: "release");

        async Task<PagedPayload<TaskListItemPayload>> GetList(string url)
        {
            var payload = await admin.GetFromJsonAsync<PagedPayload<TaskListItemPayload>>(url);
            Assert.NotNull(payload);
            return payload!;
        }

        var search = await GetList($"/api/tasks?q={token}&pageSize=100");
        Assert.Equal(4, search.TotalCount);
        Assert.Equal(4, search.Items.Count);

        var statusFilter = await GetList(
            $"/api/tasks?q={token}&status=Open&status=InProgress&pageSize=100");
        Assert.Equal(2, statusFilter.TotalCount);
        Assert.Contains(statusFilter.Items, x => x.Id == high.Id);
        Assert.Contains(statusFilter.Items, x => x.Id == low.Id);

        var priorityFilter = await GetList(
            $"/api/tasks?q={token}&priority=Urgent&pageSize=100");
        Assert.Single(priorityFilter.Items);
        Assert.Equal(urgent.Id, priorityFilter.Items[0].Id);

        var tagFilter = await GetList($"/api/tasks?q={token}&tag=release&pageSize=100");
        Assert.Equal(3, tagFilter.TotalCount);
        Assert.DoesNotContain(tagFilter.Items, x => x.Id == low.Id);

        var dueBefore = await GetList(
            $"/api/tasks?q={token}&dueBefore={today:yyyy-MM-dd}&pageSize=100");
        Assert.Equal(2, dueBefore.TotalCount);
        Assert.DoesNotContain(dueBefore.Items, x => x.Id == normal.Id);

        var dueAfter = await GetList(
            $"/api/tasks?q={token}&dueAfter={today:yyyy-MM-dd}&pageSize=100");
        Assert.Single(dueAfter.Items);
        Assert.Equal(low.Id, dueAfter.Items[0].Id);

        var overdue = await GetList($"/api/tasks?q={token}&overdue=true&pageSize=100");
        Assert.Single(overdue.Items);
        Assert.Equal(high.Id, overdue.Items[0].Id);

        using var assigneeClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            assigneeName,
            ApiTestClient.UserPassword);

        var mine = await assigneeClient.GetFromJsonAsync<PagedPayload<TaskListItemPayload>>(
            $"/api/tasks?q={token}&assigneeId=me&pageSize=100");
        Assert.NotNull(mine);
        Assert.Equal(2, mine!.TotalCount);
        Assert.Contains(mine.Items, x => x.Id == high.Id);
        Assert.Contains(mine.Items, x => x.Id == urgent.Id);

        var paged = await GetList($"/api/tasks?q={token}&pageSize=2&page=2");
        Assert.Equal(4, paged.TotalCount);
        Assert.Equal(2, paged.Page);
        Assert.Equal(2, paged.PageSize);
        Assert.Equal(2, paged.TotalPages);
        Assert.Equal(2, paged.Items.Count);

        var clamped = await GetList($"/api/tasks?q={token}&pageSize=1000");
        Assert.Equal(100, clamped.PageSize);

        var byPriority = await GetList($"/api/tasks?q={token}&sort=priority&pageSize=100");
        Assert.Equal(
            new[] { urgent.Id, high.Id, normal.Id, low.Id },
            byPriority.Items.Select(x => x.Id));

        var byDue = await GetList($"/api/tasks?q={token}&sort=due&pageSize=100");
        Assert.Equal(
            new[] { urgent.Id, high.Id, low.Id, normal.Id },
            byDue.Items.Select(x => x.Id));

        var byCreated = await GetList($"/api/tasks?q={token}&sort=created&pageSize=100");
        Assert.Equal(
            new[] { normal.Id, urgent.Id, low.Id, high.Id },
            byCreated.Items.Select(x => x.Id));

        var byActivity = await GetList($"/api/tasks?q={token}&pageSize=100");
        Assert.Equal(
            byCreated.Items.Select(x => x.Id),
            byActivity.Items.Select(x => x.Id));

        var forumScoped = await GetList(
            $"/api/forums/{forum.Id}/tasks?q={token}&pageSize=100");
        Assert.Equal(4, forumScoped.TotalCount);

        var invalidSort = await admin.GetAsync($"/api/tasks?q={token}&sort=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

        var invalidAssignee = await admin.GetAsync($"/api/tasks?assigneeId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignee.StatusCode);

        var missingForum = await admin.GetAsync(
            $"/api/forums/{Guid.NewGuid()}/tasks");
        Assert.Equal(HttpStatusCode.NotFound, missingForum.StatusCode);
    }

    [Fact]
    public async Task Permissions_follow_the_matrix()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Perm {suffix}");

        var authorName = ApiTestClient.UniqueUserName("author");
        var assigneeName = ApiTestClient.UniqueUserName("assignee");
        var otherName = ApiTestClient.UniqueUserName("other");

        var author = await ApiTestClient.CreateUserAsync(admin, authorName);
        var assignee = await ApiTestClient.CreateUserAsync(admin, assigneeName);
        await ApiTestClient.CreateUserAsync(admin, otherName);

        using var authorClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            authorName,
            ApiTestClient.UserPassword);
        using var assigneeClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            assigneeName,
            ApiTestClient.UserPassword);
        using var otherClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            otherName,
            ApiTestClient.UserPassword);

        var task = await ApiTestClient.CreateTaskAsync(
            authorClient,
            forum.Id,
            $"Permissions {suffix}",
            assigneeId: assignee.Id);

        Assert.Equal(author.UserName, task.Author.UserName);

        var otherUpdate = await otherClient.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            FullUpdate(task, forum.Id, "Other update"));
        Assert.Equal(HttpStatusCode.Forbidden, otherUpdate.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            otherUpdate.Content.Headers.ContentType?.ToString());

        var otherStatus = await otherClient.PatchAsJsonAsync(
            $"/api/tasks/{task.Id}/status",
            new { status = "Completed" });
        Assert.Equal(HttpStatusCode.Forbidden, otherStatus.StatusCode);

        var otherDelete = await otherClient.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, otherDelete.StatusCode);

        var assigneeUpdate = await assigneeClient.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            FullUpdate(task, forum.Id, "Assignee update"));
        Assert.Equal(HttpStatusCode.Forbidden, assigneeUpdate.StatusCode);

        var beforeStatus = task.LastActivityAt;
        var assigneeStatus = await assigneeClient.PatchAsJsonAsync(
            $"/api/tasks/{task.Id}/status",
            new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.OK, assigneeStatus.StatusCode);

        var afterAssigneeStatus =
            await assigneeStatus.Content.ReadFromJsonAsync<TaskDetailPayload>();
        Assert.NotNull(afterAssigneeStatus);
        Assert.Equal("InProgress", afterAssigneeStatus!.Status);
        Assert.True(afterAssigneeStatus.LastActivityAt >= beforeStatus);

        var authorUpdate = await authorClient.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            new
            {
                forumId = forum.Id,
                title = $"Updated by author {suffix}",
                body = "Updated body",
                status = "Completed",
                priority = "Urgent",
                tag = "updated",
                dueDate = new DateOnly(2026, 12, 15),
                assigneeId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.OK, authorUpdate.StatusCode);

        var updated = await authorUpdate.Content.ReadFromJsonAsync<TaskDetailPayload>();
        Assert.NotNull(updated);
        Assert.Equal($"Updated by author {suffix}", updated!.Title);
        Assert.Equal("Completed", updated.Status);
        Assert.Equal("Urgent", updated.Priority);
        Assert.Equal("updated", updated.Tag);
        Assert.Null(updated.Assignee);

        var adminTask = await ApiTestClient.CreateTaskAsync(
            authorClient,
            forum.Id,
            $"Admin override {suffix}");

        var adminUpdate = await admin.PutAsJsonAsync(
            $"/api/tasks/{adminTask.Id}",
            FullUpdate(adminTask, forum.Id, $"Admin updated {suffix}"));
        Assert.Equal(HttpStatusCode.OK, adminUpdate.StatusCode);

        var adminDelete = await admin.DeleteAsync($"/api/tasks/{adminTask.Id}");
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.StatusCode);

        var authorDelete = await authorClient.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, authorDelete.StatusCode);

        var missing = await admin.GetAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Tasks_can_move_between_active_forums_but_not_into_archived_forums()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var source = await ApiTestClient.CreateForumAsync(admin, $"Source {suffix}");
        var target = await ApiTestClient.CreateForumAsync(admin, $"Target {suffix}");
        var archived = await ApiTestClient.CreateForumAsync(
            admin,
            $"Archived move {suffix}",
            isArchived: true);

        var task = await ApiTestClient.CreateTaskAsync(
            admin,
            source.Id,
            $"Move me {suffix}");

        var moved = await admin.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            FullUpdate(task, target.Id, task.Title));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var movedTask = await moved.Content.ReadFromJsonAsync<TaskDetailPayload>();
        Assert.NotNull(movedTask);
        Assert.Equal(target.Id, movedTask!.Forum.Id);

        var blocked = await admin.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            FullUpdate(movedTask, archived.Id, movedTask.Title));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var stillInTarget = await admin.GetFromJsonAsync<TaskDetailPayload>(
            $"/api/tasks/{task.Id}");
        Assert.NotNull(stillInTarget);
        Assert.Equal(target.Id, stillInTarget!.Forum.Id);

        var archiveAfterCreate = await ApiTestClient.CreateForumAsync(
            admin,
            $"Archive after {suffix}");
        var existing = await ApiTestClient.CreateTaskAsync(
            admin,
            archiveAfterCreate.Id,
            $"Existing {suffix}");

        var archive = await admin.PutAsJsonAsync(
            $"/api/forums/{archiveAfterCreate.Id}",
            new
            {
                name = archiveAfterCreate.Name,
                slug = archiveAfterCreate.Slug,
                description = (string?)null,
                sortOrder = archiveAfterCreate.SortOrder,
                isArchived = true,
            });

        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var editInArchived = await admin.PutAsJsonAsync(
            $"/api/tasks/{existing.Id}",
            FullUpdate(existing, archiveAfterCreate.Id, $"Edited in archive {suffix}"));
        Assert.Equal(HttpStatusCode.OK, editInArchived.StatusCode);

        var search = await admin.GetFromJsonAsync<PagedPayload<TaskListItemPayload>>(
            $"/api/tasks?q={suffix}&pageSize=100");
        Assert.NotNull(search);
        Assert.Contains(search!.Items, x => x.Id == existing.Id);
        Assert.Contains(search.Items, x => x.Id == task.Id);
    }

    [Fact]
    public async Task Deleting_a_task_cascades_replies_and_attachments()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var me = await ApiTestClient.MeAsync(admin);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Cascade {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Cascade task {suffix}");

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Replies.Add(new Reply
            {
                TaskId = task.Id,
                AuthorId = me.Id,
                Body = "Reply",
            });

            db.Attachments.Add(new Attachment
            {
                TaskId = task.Id,
                FileName = "file.txt",
                ContentType = "text/plain",
                SizeBytes = 4,
                UploadedById = me.Id,
            });

            await db.SaveChangesAsync();
        }

        var detail = await admin.GetFromJsonAsync<TaskDetailPayload>(
            $"/api/tasks/{task.Id}");
        Assert.NotNull(detail);
        Assert.Single(detail!.Attachments);
        Assert.Equal("file.txt", detail.Attachments[0].FileName);
        Assert.Equal("admin", detail.Attachments[0].UploadedBy.UserName);

        var deleted = await admin.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var verifyScope = fixture.Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await verifyDb.Replies.AnyAsync(x => x.TaskId == task.Id));
        Assert.False(await verifyDb.Attachments.AnyAsync(x => x.TaskId == task.Id));
    }

    [Fact]
    public async Task Swagger_documents_forum_and_task_endpoints()
    {
        using var client = ApiTestClient.Create(fixture.Factory);

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/forums", out _));
        Assert.True(paths.TryGetProperty("/api/forums/{idOrSlug}", out _));
        Assert.True(paths.TryGetProperty("/api/tasks", out _));
        Assert.True(paths.TryGetProperty("/api/tasks/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/tasks/{id}/status", out _));
        Assert.True(paths.TryGetProperty("/api/tasks/{taskId}/replies", out _));
        Assert.True(paths.TryGetProperty("/api/replies/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/tasks/{taskId}/attachments", out _));
        Assert.True(paths.TryGetProperty("/api/replies/{replyId}/attachments", out _));
        Assert.True(paths.TryGetProperty("/api/attachments/{id}", out _));

        var ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    private static object FullUpdate(
        TaskDetailPayload task,
        Guid forumId,
        string title) =>
        new
        {
            forumId,
            title,
            body = task.Body,
            status = task.Status,
            priority = task.Priority,
            tag = task.Tag,
            dueDate = task.DueDate,
            assigneeId = task.Assignee?.Id,
        };
}
