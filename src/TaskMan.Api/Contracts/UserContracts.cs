using TaskMan.Domain.Enums;

namespace TaskMan.Api.Contracts;

public sealed record UserResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(
    string? UserName,
    string? DisplayName,
    string? Password,
    UserRole Role = UserRole.User);

public sealed record UpdateUserRequest(
    string? DisplayName,
    UserRole? Role,
    bool? IsActive);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record AssignableUserResponse(Guid Id, string UserName, string DisplayName);
