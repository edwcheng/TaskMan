using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskMan.Domain.Attachments;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Attachments;

[Collection(ApiCollection.Name)]
public sealed class AttachmentsApiTests(ApiFixture fixture)
{
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private async Task<(HttpClient Admin, Guid TaskId)> CreateTaskAsync(string label)
    {
        var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var suffix = Suffix();
        var forum = await ApiTestClient.CreateForumAsync(admin, $"{label} {suffix}");
        var task = await ApiTestClient.CreateTaskAsync(admin, forum.Id, $"Task {suffix}");

        return (admin, task.Id);
    }

    private static int FileCount(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;

    private static string FilePath(ApiFixture fixture, Guid id)
    {
        var name = id.ToString("N");
        return Path.Combine(fixture.AttachmentsPath, name[..2], name);
    }

    [Fact]
    public async Task Upload_to_task_stores_file_and_metadata()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload task");
        using var _ = admin;

        var content = System.Text.Encoding.UTF8.GetBytes("hello attachment");

        var payload = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "notes.txt",
            content,
            "text/plain");

        Assert.Equal("notes.txt", payload.FileName);
        Assert.Equal("text/plain", payload.ContentType);
        Assert.Equal(content.Length, payload.SizeBytes);
        Assert.Equal("admin", payload.UploadedBy.UserName);

        Assert.Equal(content, File.ReadAllBytes(FilePath(fixture, payload.Id)));

        var detail = await admin.GetFromJsonAsync<TaskDetailPayload>($"/api/tasks/{taskId}");
        Assert.NotNull(detail);
        Assert.Contains(detail!.Attachments, x => x.Id == payload.Id);

        var response = await admin.GetAsync($"/api/attachments/{payload.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(
            "attachment; filename*=UTF-8''notes.txt",
            response.Content.Headers.GetValues("Content-Disposition").Single());
        Assert.Equal(content.Length, response.Content.Headers.ContentLength);

        using var anonymous = ApiTestClient.Create(fixture.Factory);
        var unauthorized = await anonymous.GetAsync($"/api/attachments/{payload.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Upload_to_reply_shows_in_reply_metadata()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload reply");
        using var _ = admin;

        var reply = await ApiTestClient.CreateReplyAsync(admin, taskId, "Discussing");

        var payload = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/replies/{reply.Id}/attachments",
            "evidence.txt",
            new byte[] { 4, 5 },
            "text/plain");

        Assert.Equal("evidence.txt", payload.FileName);

        var page = await ApiTestClient.GetReplyPageAsync(admin, taskId);

        var item = page.Items.Single();
        Assert.Equal(reply.Id, item.Id);
        Assert.Single(item.Attachments);
        Assert.Equal(payload.Id, item.Attachments[0].Id);
        Assert.Equal("evidence.txt", item.Attachments[0].FileName);
        Assert.Equal(2, item.Attachments[0].SizeBytes);
    }

    [Fact]
    public async Task Upload_validates_the_request()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload validate");
        using var _ = admin;

        var missingTask = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/tasks/{Guid.NewGuid()}/attachments",
            "a.txt",
            new byte[] { 1 },
            "text/plain");

        Assert.Equal(HttpStatusCode.NotFound, missingTask.StatusCode);

        var reply = await ApiTestClient.CreateReplyAsync(admin, taskId, "Discussing");

        var missingReply = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/replies/{Guid.NewGuid()}/attachments",
            "a.txt",
            new byte[] { 1 },
            "text/plain");

        Assert.Equal(HttpStatusCode.NotFound, missingReply.StatusCode);

        using var form = new System.Net.Http.MultipartFormDataContent();
        form.Add(new System.Net.Http.StringContent("x"), "other", "other.txt");
        var noFile = await admin.PostAsync($"/api/tasks/{taskId}/attachments", form);

        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);

        // A part without a filename must be rejected, not silently accepted.
        using var emptyNameForm = new System.Net.Http.MultipartFormDataContent();
        emptyNameForm.Add(new System.Net.Http.ByteArrayContent(new byte[] { 1 }), "file");
        var emptyName = await admin.PostAsync(
            $"/api/tasks/{taskId}/attachments",
            emptyNameForm);

        Assert.Equal(HttpStatusCode.BadRequest, emptyName.StatusCode);

        var longContentType = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "a.txt",
            new byte[] { 1 },
            "application/" + new string('x', AttachmentRules.MaxContentTypeLength));

        Assert.Equal(HttpStatusCode.BadRequest, longContentType.StatusCode);
    }

    [Fact]
    public async Task Upload_enforces_the_ten_per_parent_limit()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload limit");
        using var _ = admin;

        for (var i = 0; i < AttachmentRules.MaxAttachmentsPerParent; i++)
        {
            await ApiTestClient.UploadAttachmentAsync(
                admin,
                $"/api/tasks/{taskId}/attachments",
                $"file-{i}.txt",
                new byte[] { (byte)i },
                "text/plain");
        }

        var eleven = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "file-11.txt",
            new byte[] { 1 },
            "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, eleven.StatusCode);
    }

    [Fact]
    public async Task Upload_over_the_file_size_limit_is_rejected_and_leaves_no_traces()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload size");
        using var _ = admin;

        var before = FileCount(fixture.AttachmentsPath);

        var tooLarge = new byte[AttachmentRules.MaxFileSizeBytes + 1];
        var response = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "big.bin",
            tooLarge,
            "application/octet-stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var detail = await admin.GetFromJsonAsync<TaskDetailPayload>($"/api/tasks/{taskId}");
        Assert.NotNull(detail);
        Assert.Empty(detail!.Attachments);
        Assert.Equal(before, FileCount(fixture.AttachmentsPath));
    }

    [Fact]
    public async Task Upload_over_the_request_size_limit_is_payload_too_large()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload request size");
        using var _ = admin;

        var tooLarge = new byte[30 * 1024 * 1024 + 1];
        var response = await ApiTestClient.PostMultipartAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "huge.bin",
            tooLarge,
            "application/octet-stream");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Inline_serving_is_restricted_to_the_raster_allowlist()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload inline");
        using var _ = admin;

        var png = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "photo.png",
            new byte[] { 137, 80, 78, 71 },
            "image/png");

        var inline = await admin.GetAsync($"/api/attachments/{png.Id}?inline=1");

        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("image/png", inline.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "inline; filename*=UTF-8''photo.png",
            inline.Content.Headers.GetValues("Content-Disposition").Single());

        var webp = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "anim.webp",
            new byte[] { 1 },
            "image/webp");

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/attachments/{webp.Id}?inline=1")).StatusCode);

        var gif = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "anim.gif",
            new byte[] { 1 },
            "image/gif");

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/attachments/{gif.Id}?inline=1")).StatusCode);

        var svg = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "script.svg",
            new byte[] { 1 },
            "image/svg+xml");

        var svgInline = await admin.GetAsync($"/api/attachments/{svg.Id}?inline=1");
        Assert.Equal(HttpStatusCode.BadRequest, svgInline.StatusCode);

        var svgDownload = await admin.GetAsync($"/api/attachments/{svg.Id}");
        Assert.Equal(HttpStatusCode.OK, svgDownload.StatusCode);

        var mismatch = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "lying.png",
            new byte[] { 1 },
            "image/jpeg");

        var mismatchInline = await admin.GetAsync($"/api/attachments/{mismatch.Id}?inline=1");
        Assert.Equal(HttpStatusCode.BadRequest, mismatchInline.StatusCode);
    }

    [Fact]
    public async Task Download_missing_file_is_not_found()
    {
        var (admin, taskId) = await CreateTaskAsync("Download missing");
        using var _ = admin;

        var payload = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "gone.txt",
            new byte[] { 1 },
            "text/plain");

        File.Delete(FilePath(fixture, payload.Id));

        var response = await admin.GetAsync($"/api/attachments/{payload.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_enforces_the_permission_matrix()
    {
        var (admin, taskId) = await CreateTaskAsync("Delete matrix");
        using var _ = admin;

        var uploaderName = ApiTestClient.UniqueUserName("uploader");
        await ApiTestClient.CreateUserAsync(admin, uploaderName);
        using var uploader = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            uploaderName,
            ApiTestClient.UserPassword);

        var strangerName = ApiTestClient.UniqueUserName("stranger");
        await ApiTestClient.CreateUserAsync(admin, strangerName);
        using var stranger = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            strangerName,
            ApiTestClient.UserPassword);

        // Task parent author is admin.
        var firstUpload = await ApiTestClient.UploadAttachmentAsync(
            uploader,
            $"/api/tasks/{taskId}/attachments",
            "uploaded.txt",
            new byte[] { 1 },
            "text/plain");

        var forbidden = await stranger.DeleteAsync($"/api/attachments/{firstUpload.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var byParentAuthor = await admin.DeleteAsync($"/api/attachments/{firstUpload.Id}");
        Assert.Equal(HttpStatusCode.NoContent, byParentAuthor.StatusCode);

        Assert.False(File.Exists(FilePath(fixture, firstUpload.Id)));

        // The uploader may delete their own upload.
        var secondUpload = await ApiTestClient.UploadAttachmentAsync(
            uploader,
            $"/api/tasks/{taskId}/attachments",
            "uploaded-again.txt",
            new byte[] { 2 },
            "text/plain");

        var byUploader = await uploader.DeleteAsync($"/api/attachments/{secondUpload.Id}");
        Assert.Equal(HttpStatusCode.NoContent, byUploader.StatusCode);
    }

    [Fact]
    public async Task Delete_uses_the_reply_author_for_reply_attachments()
    {
        var (admin, taskId) = await CreateTaskAsync("Delete reply");
        using var _ = admin;

        var authorName = ApiTestClient.UniqueUserName("replyauth");
        await ApiTestClient.CreateUserAsync(admin, authorName);
        using var author = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            authorName,
            ApiTestClient.UserPassword);

        var uploaderName = ApiTestClient.UniqueUserName("uploader");
        await ApiTestClient.CreateUserAsync(admin, uploaderName);
        using var uploader = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            uploaderName,
            ApiTestClient.UserPassword);

        var strangerName = ApiTestClient.UniqueUserName("stranger");
        await ApiTestClient.CreateUserAsync(admin, strangerName);
        using var stranger = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            strangerName,
            ApiTestClient.UserPassword);

        var reply = await ApiTestClient.CreateReplyAsync(author, taskId, "Discussion");

        var payload = await ApiTestClient.UploadAttachmentAsync(
            uploader,
            $"/api/replies/{reply.Id}/attachments",
            "reply-file.txt",
            new byte[] { 3 },
            "text/plain");

        var forbidden = await stranger.DeleteAsync($"/api/attachments/{payload.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var byUploader = await uploader.DeleteAsync($"/api/attachments/{payload.Id}");
        Assert.Equal(HttpStatusCode.NoContent, byUploader.StatusCode);
    }

    [Fact]
    public async Task Upload_rolls_back_the_file_when_the_insert_fails()
    {
        var (admin, taskId) = await CreateTaskAsync("Upload rollback");
        using var _ = admin;

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A failing trigger forces the metadata insert to error after the file
        // has been written. (REVOKE would not work: the test database runs as
        // a superuser, which bypasses permission checks.)
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_attachment_insert() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'simulated failure';
                END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_attachment_insert
                BEFORE INSERT ON "Attachments"
                FOR EACH ROW
                EXECUTE FUNCTION fail_attachment_insert();
            """);

        try
        {
            var before = FileCount(fixture.AttachmentsPath);

            var response = await ApiTestClient.PostMultipartAsync(
                admin,
                $"/api/tasks/{taskId}/attachments",
                "locked.txt",
                new byte[] { 1 },
                "text/plain");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, FileCount(fixture.AttachmentsPath));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS fail_attachment_insert ON "Attachments";
                DROP FUNCTION IF EXISTS fail_attachment_insert();
                """);
        }

        var detail = await admin.GetFromJsonAsync<TaskDetailPayload>($"/api/tasks/{taskId}");
        Assert.NotNull(detail);
        Assert.Empty(detail!.Attachments);
    }

    [Fact]
    public async Task Task_delete_removes_replies_and_all_attachment_files()
    {
        var (admin, taskId) = await CreateTaskAsync("Task cascade");
        using var _ = admin;

        var strangerName = ApiTestClient.UniqueUserName("stranger");
        await ApiTestClient.CreateUserAsync(admin, strangerName);
        using var stranger = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            strangerName,
            ApiTestClient.UserPassword);

        var reply = await ApiTestClient.CreateReplyAsync(stranger, taskId, "Discussion");

        var taskAttachment = await ApiTestClient.UploadAttachmentAsync(
            admin,
            $"/api/tasks/{taskId}/attachments",
            "task-file.bin",
            new byte[] { 1 },
            "application/octet-stream");

        var replyAttachment = await ApiTestClient.UploadAttachmentAsync(
            stranger,
            $"/api/replies/{reply.Id}/attachments",
            "reply-file.bin",
            new byte[] { 2 },
            "application/octet-stream");

        var response = await admin.DeleteAsync($"/api/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var missing = await admin.GetAsync($"/api/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Replies.CountAsync(x => x.TaskId == taskId));
        Assert.Equal(0, await db.Attachments.CountAsync(x => x.TaskId == taskId));
        Assert.Equal(0, await db.Attachments.CountAsync(x => x.ReplyId == reply.Id));

        Assert.False(File.Exists(FilePath(fixture, taskAttachment.Id)));
        Assert.False(File.Exists(FilePath(fixture, replyAttachment.Id)));
    }
}
