namespace TaskMan.Infrastructure.Auth;

public static class AuthDefaults
{
    public const string CookieName = "tm_session";

    public const string SecurityStampClaim = "security_stamp";

    public const string SessionStartedClaim = "session_started";

    public const string CsrfHeaderName = "X-Requested-With";

    public const string CsrfHeaderValue = "TaskMan";
}
