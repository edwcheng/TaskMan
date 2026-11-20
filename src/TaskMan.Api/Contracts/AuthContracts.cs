namespace TaskMan.Api.Contracts;

public sealed record LoginRequest(string? UserName, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
