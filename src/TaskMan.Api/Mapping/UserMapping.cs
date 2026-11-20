using TaskMan.Api.Contracts;
using TaskMan.Domain.Entities;

namespace TaskMan.Api.Mapping;

public static class UserMapping
{
    public static UserResponse ToResponse(this User user) =>
        new(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt);

    public static AssignableUserResponse ToAssignableResponse(this User user) =>
        new(user.Id, user.UserName, user.DisplayName);
}
