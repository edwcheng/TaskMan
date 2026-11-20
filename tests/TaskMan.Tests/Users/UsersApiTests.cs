using System.Net;
using System.Net.Http.Json;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Users;

[Collection(ApiCollection.Name)]
public sealed class UsersApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task Admin_can_create_list_search_and_get_users()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("create");

        var created = await ApiTestClient.CreateUserAsync(
            admin,
            userName,
            displayName: "Created User");

        Assert.Equal(userName, created.UserName);
        Assert.Equal("Created User", created.DisplayName);
        Assert.Equal("User", created.Role);
        Assert.True(created.IsActive);
        Assert.Null(created.LastLoginAt);

        var byId = await admin.GetFromJsonAsync<UserPayload>(
            $"/api/users/{created.Id}");
        Assert.NotNull(byId);
        Assert.Equal(created.Id, byId!.Id);

        var search = await admin.GetFromJsonAsync<PagedPayload<UserPayload>>(
            $"/api/users?q={Uri.EscapeDataString(userName)}");
        Assert.NotNull(search);
        Assert.Contains(search!.Items, x => x.Id == created.Id);
        Assert.Equal(1, search.TotalCount);
        Assert.Equal(1, search.TotalPages);
    }

    [Fact]
    public async Task Usernames_are_unique_case_insensitively()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("dupe");

        await ApiTestClient.CreateUserAsync(admin, userName);

        var duplicate = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName = userName.ToUpperInvariant(),
                displayName = "Duplicate",
                password = ApiTestClient.UserPassword,
                role = "User",
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            duplicate.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Create_validates_username_display_name_password_and_role()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);

        var invalidUserName = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName = "no spaces allowed",
                displayName = "User",
                password = ApiTestClient.UserPassword,
                role = "User",
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidUserName.StatusCode);

        var shortPassword = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName = ApiTestClient.UniqueUserName("short"),
                displayName = "User",
                password = "short",
                role = "User",
            });

        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);

        var invalidRole = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName = ApiTestClient.UniqueUserName("role"),
                displayName = "User",
                password = ApiTestClient.UserPassword,
                role = "SuperUser",
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_a_user_and_role_changes_invalidate_sessions()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("promote");
        var created = await ApiTestClient.CreateUserAsync(admin, userName);

        using var userClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var update = await admin.PutAsJsonAsync(
            $"/api/users/{created.Id}",
            new
            {
                displayName = "Promoted User",
                role = "Admin",
                isActive = true,
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await update.Content.ReadFromJsonAsync<UserPayload>();
        Assert.NotNull(updated);
        Assert.Equal("Promoted User", updated!.DisplayName);
        Assert.Equal("Admin", updated.Role);

        var me = await userClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_user_invalidates_their_session_and_blocks_login()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("deactive");
        var created = await ApiTestClient.CreateUserAsync(admin, userName);

        using var userClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var delete = await admin.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var me = await userClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var loginAgain = ApiTestClient.Create(fixture.Factory);
        var login = await ApiTestClient.LoginAsync(
            loginAgain,
            userName,
            ApiTestClient.UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var fetched = await admin.GetFromJsonAsync<UserPayload>(
            $"/api/users/{created.Id}");
        Assert.NotNull(fetched);
        Assert.False(fetched!.IsActive);
    }

    [Fact]
    public async Task Admin_cannot_demote_or_deactivate_their_own_account()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var me = await ApiTestClient.MeAsync(admin);

        var demote = await admin.PutAsJsonAsync(
            $"/api/users/{me.Id}",
            new
            {
                displayName = me.DisplayName,
                role = "User",
                isActive = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);

        var deactivate = await admin.PutAsJsonAsync(
            $"/api/users/{me.Id}",
            new
            {
                displayName = me.DisplayName,
                role = "Admin",
                isActive = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, deactivate.StatusCode);

        var delete = await admin.DeleteAsync($"/api/users/{me.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);

        var stillMe = await ApiTestClient.MeAsync(admin);
        Assert.Equal("Admin", stillMe.Role);
        Assert.True(stillMe.IsActive);
    }

    [Fact]
    public async Task Reset_password_invalidates_sessions_and_allows_the_new_password()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("reset");
        var created = await ApiTestClient.CreateUserAsync(admin, userName);

        using var userClient = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var reset = await admin.PostAsJsonAsync(
            $"/api/users/{created.Id}/reset-password",
            new { newPassword = ApiTestClient.ResetPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var me = await userClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var oldPasswordClient = ApiTestClient.Create(fixture.Factory);
        var oldPassword = await ApiTestClient.LoginAsync(
            oldPasswordClient,
            userName,
            ApiTestClient.UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        using var newPasswordClient = ApiTestClient.Create(fixture.Factory);
        var newPassword = await ApiTestClient.LoginAsync(
            newPasswordClient,
            userName,
            ApiTestClient.ResetPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Reset_password_validates_the_new_password()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var created = await ApiTestClient.CreateUserAsync(
            admin,
            ApiTestClient.UniqueUserName("weak"));

        var reset = await admin.PostAsJsonAsync(
            $"/api/users/{created.Id}/reset-password",
            new { newPassword = "weak" });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task Assignable_users_include_only_active_accounts()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var activeName = ApiTestClient.UniqueUserName("assignon");
        var active = await ApiTestClient.CreateUserAsync(admin, activeName);

        var inactiveName = ApiTestClient.UniqueUserName("assignoff");
        var inactive = await ApiTestClient.CreateUserAsync(admin, inactiveName);
        await admin.DeleteAsync($"/api/users/{inactive.Id}");

        var users = await admin.GetFromJsonAsync<List<AssignableUserPayload>>(
            "/api/users/assignable");

        Assert.NotNull(users);
        Assert.Contains(users!, x => x.Id == active.Id);
        Assert.DoesNotContain(users!, x => x.Id == inactive.Id);
        Assert.All(users!, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayName)));
    }

    [Fact]
    public async Task Non_admin_users_can_list_assignable_users_but_not_manage_users()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("member");
        await ApiTestClient.CreateUserAsync(admin, userName);

        using var member = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var assignable = await member.GetAsync("/api/users/assignable");
        Assert.Equal(HttpStatusCode.OK, assignable.StatusCode);

        var list = await member.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            list.Content.Headers.ContentType?.ToString());

        var create = await member.PostAsJsonAsync(
            "/api/users",
            new
            {
                userName = ApiTestClient.UniqueUserName("forbidden"),
                displayName = "Forbidden",
                password = ApiTestClient.UserPassword,
                role = "User",
            });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Admin_resetting_their_own_password_keeps_the_current_session()
    {
        using var seedAdmin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("selfreset");
        var created = await ApiTestClient.CreateUserAsync(
            seedAdmin,
            userName,
            role: "Admin");

        using var first = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        using var second = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var reset = await first.PostAsJsonAsync(
            $"/api/users/{created.Id}/reset-password",
            new { newPassword = ApiTestClient.ResetPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var firstMe = await first.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, firstMe.StatusCode);

        var secondMe = await second.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, secondMe.StatusCode);

        using var newPasswordClient = ApiTestClient.Create(fixture.Factory);
        var newPassword = await ApiTestClient.LoginAsync(
            newPasswordClient,
            userName,
            ApiTestClient.ResetPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Unknown_users_return_not_found()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var id = Guid.NewGuid();

        var get = await admin.GetAsync($"/api/users/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var update = await admin.PutAsJsonAsync(
            $"/api/users/{id}",
            new
            {
                displayName = "Missing",
                role = "User",
                isActive = true,
            });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await admin.DeleteAsync($"/api/users/{id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task List_can_filter_by_role_and_active_state()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var adminName = ApiTestClient.UniqueUserName("filteradmin");
        var createdAdmin = await ApiTestClient.CreateUserAsync(
            admin,
            adminName,
            role: "Admin");

        var users = await admin.GetFromJsonAsync<PagedPayload<UserPayload>>(
            "/api/users?role=Admin&isActive=true&pageSize=100");

        Assert.NotNull(users);
        Assert.Contains(users!.Items, x => x.Id == createdAdmin.Id);
        Assert.All(users.Items, x =>
        {
            Assert.Equal("Admin", x.Role);
            Assert.True(x.IsActive);
        });
    }
}
