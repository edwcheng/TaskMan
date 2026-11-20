using System.Net;
using System.Net.Http.Json;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Forums;

[Collection(ApiCollection.Name)]
public sealed class ForumsApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task Admin_can_create_ordered_forums_with_generated_slugs_and_counts()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Zulu {suffix}";

        var first = await ApiTestClient.CreateForumAsync(admin, name, sortOrder: 7101);
        var duplicate = await ApiTestClient.CreateForumAsync(admin, name, sortOrder: 7102);

        Assert.StartsWith($"zulu-{suffix}", first.Slug);
        Assert.Equal($"{first.Slug}-2", duplicate.Slug);

        await ApiTestClient.CreateTaskAsync(
            admin,
            first.Id,
            $"Open task {suffix}",
            status: "Open");
        await ApiTestClient.CreateTaskAsync(
            admin,
            first.Id,
            $"Done task {suffix}",
            status: "Completed");

        var forums = await admin.GetFromJsonAsync<List<ForumPayload>>("/api/forums");
        Assert.NotNull(forums);

        var loaded = forums!.Single(x => x.Id == first.Id);
        Assert.Equal(2, loaded.TaskCount);
        Assert.Equal(1, loaded.OpenTaskCount);
        Assert.NotNull(loaded.LastActivityAt);

        var second = await ApiTestClient.CreateForumAsync(
            admin,
            $"Alpha {suffix}",
            sortOrder: 7100);

        forums = await admin.GetFromJsonAsync<List<ForumPayload>>("/api/forums");
        Assert.NotNull(forums);

        var secondIndex = forums!.FindIndex(x => x.Id == second.Id);
        var firstIndex = forums.FindIndex(x => x.Id == first.Id);
        Assert.True(secondIndex < firstIndex);
    }

    [Fact]
    public async Task Archived_forums_are_hidden_only_when_requested()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var archived = await ApiTestClient.CreateForumAsync(
            admin,
            $"Archived {Guid.NewGuid():N}",
            isArchived: true);

        var all = await admin.GetFromJsonAsync<List<ForumPayload>>("/api/forums");
        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == archived.Id && x.IsArchived);

        var active = await admin.GetFromJsonAsync<List<ForumPayload>>(
            "/api/forums?includeArchived=false");
        Assert.NotNull(active);
        Assert.DoesNotContain(active!, x => x.Id == archived.Id);
    }

    [Fact]
    public async Task Forum_detail_supports_id_and_slug()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var forum = await ApiTestClient.CreateForumAsync(
            admin,
            $"Detail {Guid.NewGuid():N}");

        var byId = await admin.GetFromJsonAsync<ForumPayload>(
            $"/api/forums/{forum.Id}");
        Assert.NotNull(byId);
        Assert.Equal(forum.Id, byId!.Id);

        var bySlug = await admin.GetFromJsonAsync<ForumPayload>(
            $"/api/forums/{forum.Slug}");
        Assert.NotNull(bySlug);
        Assert.Equal(forum.Id, bySlug!.Id);

        var missing = await admin.GetAsync($"/api/forums/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_a_forum_and_slug_conflicts_are_rejected()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Before {suffix}");
        var other = await ApiTestClient.CreateForumAsync(admin, $"Other {suffix}");

        var update = await admin.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = $"After {suffix}",
                slug = $"after-{suffix}",
                description = "  Updated description.  ",
                sortOrder = 42,
                isArchived = true,
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await update.Content.ReadFromJsonAsync<ForumPayload>();
        Assert.NotNull(updated);
        Assert.Equal($"after-{suffix}", updated!.Slug);
        Assert.Equal("Updated description.", updated.Description);
        Assert.Equal(42, updated.SortOrder);
        Assert.True(updated.IsArchived);

        var conflict = await admin.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = updated.Name,
                slug = other.Slug,
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var invalid = await admin.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = updated.Name,
                slug = "Not A Slug",
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var generated = await admin.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = $"Generated {suffix}",
                slug = (string?)null,
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);

        var generatedForum = await generated.Content.ReadFromJsonAsync<ForumPayload>();
        Assert.NotNull(generatedForum);
        Assert.StartsWith($"generated-{suffix}", generatedForum!.Slug);
    }

    [Fact]
    public async Task Forum_create_validates_fields_and_rejects_duplicate_slugs()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var blankName = await admin.PostAsJsonAsync(
            "/api/forums",
            new
            {
                name = " ",
                slug = (string?)null,
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);

        var invalidSlug = await admin.PostAsJsonAsync(
            "/api/forums",
            new
            {
                name = $"Invalid {suffix}",
                slug = "Invalid Slug",
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidSlug.StatusCode);

        var created = await ApiTestClient.CreateForumAsync(
            admin,
            $"Duplicate {suffix}",
            slug: $"duplicate-{suffix}");

        var duplicate = await admin.PostAsJsonAsync(
            "/api/forums",
            new
            {
                name = $"Duplicate again {suffix}",
                slug = created.Slug,
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Forum_delete_requires_an_empty_forum_and_admin_access()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var forum = await ApiTestClient.CreateForumAsync(admin, $"Delete {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(
            admin,
            forum.Id,
            $"Blocking task {suffix}");

        var blocked = await admin.DeleteAsync($"/api/forums/{forum.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await admin.DeleteAsync($"/api/tasks/{task.Id}");

        var deleted = await admin.DeleteAsync($"/api/forums/{forum.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await admin.GetAsync($"/api/forums/{forum.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var memberName = ApiTestClient.UniqueUserName("forumuser");
        await ApiTestClient.CreateUserAsync(admin, memberName);

        using var member = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            memberName,
            ApiTestClient.UserPassword);

        var create = await member.PostAsJsonAsync(
            "/api/forums",
            new
            {
                name = $"Forbidden {suffix}",
                slug = (string?)null,
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var update = await member.PutAsJsonAsync(
            $"/api/forums/{forum.Id}",
            new
            {
                name = "Forbidden",
                slug = "forbidden",
                description = (string?)null,
                sortOrder = 1,
                isArchived = false,
            });

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var delete = await member.DeleteAsync($"/api/forums/{forum.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
