using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Auth;

public static class AuthSession
{
    public static Task SignInAsync(HttpContext context, User user, DateTimeOffset sessionStartedAt) =>
        context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionClaims.CreatePrincipal(user, sessionStartedAt),
            new AuthenticationProperties { IsPersistent = false });

    public static Task SignOutAsync(HttpContext context) =>
        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
