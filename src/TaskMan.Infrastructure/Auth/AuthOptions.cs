namespace TaskMan.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool CookieSecure { get; set; }

    public int SessionIdleHours { get; set; } = 8;

    public int SessionAbsoluteDays { get; set; } = 7;

    public int LoginPermitLimit { get; set; } = 10;

    public int LoginWindowSeconds { get; set; } = 60;

    public void Validate()
    {
        if (SessionIdleHours <= 0)
        {
            throw new InvalidOperationException("Auth:SessionIdleHours must be greater than zero.");
        }

        if (SessionAbsoluteDays <= 0)
        {
            throw new InvalidOperationException("Auth:SessionAbsoluteDays must be greater than zero.");
        }

        if (LoginPermitLimit <= 0)
        {
            throw new InvalidOperationException("Auth:LoginPermitLimit must be greater than zero.");
        }

        if (LoginWindowSeconds <= 0)
        {
            throw new InvalidOperationException("Auth:LoginWindowSeconds must be greater than zero.");
        }
    }
}
