using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskMan.Infrastructure.Auth;
using TaskMan.Tests.Infrastructure;

namespace TaskMan.Tests.Support;

internal static class ApiTestClient
{
    public const string UserPassword = "test-user-password";
    public const string ChangedPassword = "changed-password-123";
    public const string ResetPassword = "reset-password-123";

    public static HttpClient Create(
        TaskManApiFactory factory,
        bool includeCsrfHeader = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        if (includeCsrfHeader)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                AuthDefaults.CsrfHeaderName,
                AuthDefaults.CsrfHeaderValue);
        }

        return client;
    }

    public static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string userName,
        string password) =>
        client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName, password });

    public static async Task<HttpClient> LoginClientAsync(
        TaskManApiFactory factory,
        string userName,
        string password)
    {
        var client = Create(factory);
        var response = await LoginAsync(client, userName, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }

    public static async Task<HttpClient> AdminClientAsync(TaskManApiFactory factory) =>
        await LoginClientAsync(
            factory,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

    public static async Task<UserPayload> CreateUserAsync(
        HttpClient adminClient,
        string userName,
        string password = UserPassword,
        string role = "User",
        string? displayName = null)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName,
                displayName = displayName ?? userName,
                password,
                role,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UserPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static string UniqueUserName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 8, 32)];

    public static async Task<ForumPayload> CreateForumAsync(
        HttpClient adminClient,
        string name,
        string? slug = null,
        string? description = null,
        int? sortOrder = null,
        bool? isArchived = null)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/forums",
            new
            {
                name,
                slug,
                description,
                sortOrder,
                isArchived,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ForumPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static async Task<TaskDetailPayload> CreateTaskAsync(
        HttpClient client,
        Guid forumId,
        string title,
        string body = "Task body",
        string? status = null,
        string? priority = null,
        string? tag = null,
        DateOnly? dueDate = null,
        Guid? assigneeId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/tasks",
            new
            {
                forumId,
                title,
                body,
                status,
                priority,
                tag,
                dueDate,
                assigneeId,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskDetailPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static async Task<UserPayload> MeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UserPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static async Task<HttpResponseMessage> PostMultipartAsync(
        HttpClient client,
        string path,
        string fileName,
        byte[] content,
        string contentType)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        form.Add(fileContent, "file", fileName);

        // Awaiting before the form is disposed keeps the request content alive
        // while the client sends it.
        return await client.PostAsync(path, form);
    }

    public static async Task<AttachmentPayload> UploadAttachmentAsync(
        HttpClient client,
        string path,
        string fileName,
        byte[] content,
        string contentType)
    {
        var response = await PostMultipartAsync(client, path, fileName, content, contentType);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AttachmentPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static async Task<ReplyPayload> CreateReplyAsync(
        HttpClient client,
        Guid taskId,
        string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/replies",
            new { body });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReplyPayload>();
        Assert.NotNull(payload);

        return payload!;
    }

    public static async Task<PagedPayload<ReplyPayload>> GetReplyPageAsync(
        HttpClient client,
        Guid taskId,
        int page = 1,
        int pageSize = 50)
    {
        var response = await client.GetAsync(
            $"/api/tasks/{taskId}/replies?page={page}&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PagedPayload<ReplyPayload>>();
        Assert.NotNull(payload);

        return payload!;
    }
}

internal sealed record UserPayload(
    Guid Id,
    string UserName,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

internal sealed record PagedPayload<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

internal sealed record AssignableUserPayload(Guid Id, string UserName, string DisplayName);

internal sealed record ForumPayload(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int SortOrder,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    int TaskCount,
    int OpenTaskCount,
    DateTimeOffset? LastActivityAt);

internal sealed record ForumSummaryPayload(
    Guid Id,
    string Name,
    string Slug,
    bool IsArchived);

internal sealed record UserSummaryPayload(
    Guid Id,
    string UserName,
    string DisplayName);

internal sealed record AttachmentPayload(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    UserSummaryPayload UploadedBy,
    DateTimeOffset CreatedAt);

internal sealed record TaskListItemPayload(
    Guid Id,
    long Number,
    string Title,
    string Status,
    string Priority,
    string? Tag,
    DateOnly? DueDate,
    ForumSummaryPayload Forum,
    UserSummaryPayload Author,
    UserSummaryPayload? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int ReplyCount,
    int AttachmentCount);

internal sealed record TaskDetailPayload(
    Guid Id,
    long Number,
    string Title,
    string Body,
    string Status,
    string Priority,
    string? Tag,
    DateOnly? DueDate,
    ForumSummaryPayload Forum,
    UserSummaryPayload Author,
    UserSummaryPayload? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActivityAt,
    int ReplyCount,
    IReadOnlyList<AttachmentPayload> Attachments);

internal sealed record ReplyPayload(
    Guid Id,
    string Body,
    UserSummaryPayload Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AttachmentPayload> Attachments);

