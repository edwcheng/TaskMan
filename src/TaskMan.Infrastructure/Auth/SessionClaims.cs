using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Auth;

public static class SessionClaims
{
    public static ClaimsPrincipal CreatePrincipal(User user, DateTimeOffset sessionStartedAt)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AuthDefaults.SecurityStampClaim, user.SecurityStamp.ToString()),
            new(
                AuthDefaults.SessionStartedClaim,
                sessionStartedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ];

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static DateTimeOffset GetSessionStarted(
        ClaimsPrincipal principal,
        DateTimeOffset fallback)
    {
        var value = principal.FindFirst(AuthDefaults.SessionStartedClaim)?.Value;

        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : fallback;
    }
}
