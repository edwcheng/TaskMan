using TaskMan.Domain.Entities;
using TaskMan.Domain.Enums;
using TaskMan.Infrastructure.Auth;

namespace TaskMan.Tests.Auth;

public sealed class SessionValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    [Fact]
    public void Valid_session_passes()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now.AddHours(-1));

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Null(failure);
    }

    [Fact]
    public void Session_exactly_at_the_absolute_lifetime_passes()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now - AbsoluteLifetime);

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Null(failure);
    }

    [Fact]
    public void Session_past_the_absolute_lifetime_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(
            user,
            Now - AbsoluteLifetime - TimeSpan.FromSeconds(1));

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("Session reached its absolute lifetime.", failure);
    }

    [Fact]
    public void Missing_user_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);

        var failure = SessionValidator.Validate(
            null,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("User no longer exists.", failure);
    }

    [Fact]
    public void Inactive_user_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);
        user.IsActive = false;

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("User is inactive.", failure);
    }

    [Fact]
    public void Security_stamp_change_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);
        user.SecurityStamp = Guid.NewGuid();

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("Security stamp does not match.", failure);
    }

    [Fact]
    public void Missing_or_malformed_stamp_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);

        Assert.Equal(
            "Security stamp does not match.",
            SessionValidator.Validate(
                user,
                WithoutClaim(principal, AuthDefaults.SecurityStampClaim),
                Now,
                AbsoluteLifetime));

        Assert.Equal(
            "Security stamp does not match.",
            SessionValidator.Validate(
                user,
                WithClaim(principal, AuthDefaults.SecurityStampClaim, "not-a-guid"),
                Now,
                AbsoluteLifetime));
    }

    [Fact]
    public void Role_change_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);
        user.Role = UserRole.Admin;

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("User role changed.", failure);
    }

    [Fact]
    public void Missing_session_start_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now);

        var failure = SessionValidator.Validate(
            user,
            WithoutClaim(principal, AuthDefaults.SessionStartedClaim),
            Now,
            AbsoluteLifetime);

        Assert.Equal("Session start claim is missing.", failure);
    }

    [Fact]
    public void Session_start_in_the_future_is_rejected()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now.AddMinutes(10));

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Equal("Session start claim is in the future.", failure);
    }

    [Fact]
    public void Small_clock_skew_in_the_future_is_tolerated()
    {
        var user = CreateUser();
        var principal = SessionClaims.CreatePrincipal(user, Now.AddMinutes(2));

        var failure = SessionValidator.Validate(
            user,
            principal,
            Now,
            AbsoluteLifetime);

        Assert.Null(failure);
    }

    [Fact]
    public void Get_session_started_preserves_an_existing_claim()
    {
        var user = CreateUser();
        var startedAt = Now.AddHours(-3);
        var principal = SessionClaims.CreatePrincipal(user, startedAt);

        var resolved = SessionClaims.GetSessionStarted(principal, Now);

        Assert.Equal(startedAt.ToUnixTimeSeconds(), resolved.ToUnixTimeSeconds());
    }

    [Fact]
    public void Get_session_started_falls_back_when_the_claim_is_missing()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity());

        var resolved = SessionClaims.GetSessionStarted(principal, Now);

        Assert.Equal(Now, resolved);
    }

    private static User CreateUser() =>
        new()
        {
            UserName = "tester",
            NormalizedUserName = "TESTER",
            DisplayName = "Tester",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true,
        };

    private static System.Security.Claims.ClaimsPrincipal WithoutClaim(
        System.Security.Claims.ClaimsPrincipal principal,
        string claimType)
    {
        var identity = (System.Security.Claims.ClaimsIdentity)principal.Identity!;
        var claims = principal.Claims
            .Where(x => x.Type != claimType)
            .Select(x => new System.Security.Claims.Claim(x.Type, x.Value));

        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                claims,
                identity.AuthenticationType));
    }

    private static System.Security.Claims.ClaimsPrincipal WithClaim(
        System.Security.Claims.ClaimsPrincipal principal,
        string claimType,
        string value)
    {
        var identity = (System.Security.Claims.ClaimsIdentity)principal.Identity!;
        var claims = principal.Claims
            .Where(x => x.Type != claimType)
            .Select(x => new System.Security.Claims.Claim(x.Type, x.Value))
            .Append(new System.Security.Claims.Claim(claimType, value));

        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                claims,
                identity.AuthenticationType));
    }
}
