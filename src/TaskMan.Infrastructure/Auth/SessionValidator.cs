using System.Globalization;
using System.Security.Claims;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Auth;

public static class SessionValidator
{
    public static string? Validate(
        User? user,
        ClaimsPrincipal principal,
        DateTimeOffset now,
        TimeSpan absoluteLifetime)
    {
        if (user is null)
        {
            return "User no longer exists.";
        }

        if (!user.IsActive)
        {
            return "User is inactive.";
        }

        var stampValue = principal.FindFirst(AuthDefaults.SecurityStampClaim)?.Value;
        if (!Guid.TryParse(stampValue, out var stamp) || stamp != user.SecurityStamp)
        {
            return "Security stamp does not match.";
        }

        var roleValue = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.Equals(roleValue, user.Role.ToString(), StringComparison.Ordinal))
        {
            return "User role changed.";
        }

        var startedValue = principal.FindFirst(AuthDefaults.SessionStartedClaim)?.Value;
        if (!long.TryParse(
                startedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var startedSeconds))
        {
            return "Session start claim is missing.";
        }

        var startedAt = DateTimeOffset.FromUnixTimeSeconds(startedSeconds);

        if (startedAt > now.AddMinutes(5))
        {
            return "Session start claim is in the future.";
        }

        if (now - startedAt > absoluteLifetime)
        {
            return "Session reached its absolute lifetime.";
        }

        return null;
    }
}
