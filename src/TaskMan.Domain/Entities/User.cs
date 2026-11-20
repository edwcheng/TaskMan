using TaskMan.Domain.Enums;

namespace TaskMan.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string UserName { get; set; } = string.Empty;

    public string NormalizedUserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}
