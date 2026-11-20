using System.Security.Claims;

namespace TaskMan.Api.Security;

public static class CurrentUserExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(value, out var userId))
        {
            throw new InvalidOperationException(
                "The authenticated principal does not contain a valid user identifier.");
        }

        return userId;
    }
}
