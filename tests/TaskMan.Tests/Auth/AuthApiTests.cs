using System.Net;
using System.Net.Http.Json;
using TaskMan.Infrastructure.Auth;
using TaskMan.Tests.Infrastructure;
using TaskMan.Tests.Support;

namespace TaskMan.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class AuthApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task Login_sets_cookie_and_returns_profile()
    {
        using var client = ApiTestClient.Create(fixture.Factory);

        var response = await ApiTestClient.LoginAsync(
            client,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UserPayload>();
        Assert.NotNull(payload);
        Assert.Equal(ApiFixture.AdminUserName, payload!.UserName);
        Assert.Equal("Test Administrator", payload.DisplayName);
        Assert.Equal("Admin", payload.Role);
        Assert.True(payload.IsActive);
        Assert.NotNull(payload.LastLoginAt);

        var setCookie = string.Join(
            "\n",
            response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("tm_session=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        var me = await ApiTestClient.MeAsync(client);
        Assert.Equal(payload.Id, me.Id);
        Assert.Equal(payload.UserName, me.UserName);
    }

    [Fact]
    public async Task Unauthenticated_api_requests_return_401_without_redirect()
    {
        using var client = ApiTestClient.Create(fixture.Factory);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Null(me.Headers.Location);
        Assert.StartsWith(
            "application/problem+json",
            me.Content.Headers.ContentType?.ToString());

        var users = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, users.StatusCode);
        Assert.Null(users.Headers.Location);
    }

    [Fact]
    public async Task Non_get_api_requests_require_the_csrf_header()
    {
        using var client = ApiTestClient.Create(fixture.Factory, includeCsrfHeader: false);

        var response = await ApiTestClient.LoginAsync(
            client,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Login_rejects_unknown_inactive_and_wrong_password()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("login");

        var user = await ApiTestClient.CreateUserAsync(admin, userName);

        using var client = ApiTestClient.Create(fixture.Factory);

        var wrongPassword = await ApiTestClient.LoginAsync(
            client,
            userName,
            "definitely-wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var unknownUser = await ApiTestClient.LoginAsync(
            client,
            "missing-user",
            ApiTestClient.UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

        var deactivate = await admin.DeleteAsync($"/api/users/{user.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var inactive = await ApiTestClient.LoginAsync(
            client,
            userName,
            ApiTestClient.UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        using var client = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Change_password_rotates_stamp_and_invalidates_other_sessions()
    {
        using var admin = await ApiTestClient.AdminClientAsync(fixture.Factory);
        var userName = ApiTestClient.UniqueUserName("password");
        await ApiTestClient.CreateUserAsync(admin, userName);

        using var first = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        using var second = await ApiTestClient.LoginClientAsync(
            fixture.Factory,
            userName,
            ApiTestClient.UserPassword);

        var wrongCurrent = await first.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = "wrong-password",
                newPassword = ApiTestClient.ChangedPassword,
            });

        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var changed = await first.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = ApiTestClient.UserPassword,
                newPassword = ApiTestClient.ChangedPassword,
            });

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        var firstMe = await first.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, firstMe.StatusCode);

        var secondMe = await second.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, secondMe.StatusCode);

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
            ApiTestClient.ChangedPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Session_survives_a_new_api_host_with_the_same_data_protection_keys()
    {
        using var loginClient = ApiTestClient.Create(fixture.Factory);

        var login = await ApiTestClient.LoginAsync(
            loginClient,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sessionCookie = login.Headers
            .GetValues("Set-Cookie")
            .Single()
            .Split(';', 2)[0];

        await using var restartedFactory = fixture.CreateFactory();
        using var restartedClient = ApiTestClient.Create(restartedFactory);

        restartedClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            sessionCookie);

        var me = await ApiTestClient.MeAsync(restartedClient);

        Assert.Equal(ApiFixture.AdminUserName, me.UserName);
        Assert.Equal("Admin", me.Role);
    }

    [Fact]
    public async Task Secure_cookie_is_used_when_configured()
    {
        await using var factory = fixture.CreateFactory(
            new Dictionary<string, string?>
            {
                ["Auth:CookieSecure"] = "true",
            });

        using var client = ApiTestClient.Create(factory);

        var response = await ApiTestClient.LoginAsync(
            client,
            ApiFixture.AdminUserName,
            ApiFixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = string.Join(
            "\n",
            response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        await using var factory = fixture.CreateFactory(
            new Dictionary<string, string?>
            {
                ["Auth:LoginPermitLimit"] = "2",
            });

        using var client = ApiTestClient.Create(factory);

        var first = await ApiTestClient.LoginAsync(client, "nobody", "bad-password");
        var second = await ApiTestClient.LoginAsync(client, "nobody", "bad-password");
        var third = await ApiTestClient.LoginAsync(client, "nobody", "bad-password");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.NotEmpty(retryAfter);
        Assert.StartsWith(
            "application/problem+json",
            third.Content.Headers.ContentType?.ToString());
    }
}
