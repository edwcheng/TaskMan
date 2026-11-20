namespace TaskMan.Domain.Users;

public static class UserRules
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 32;
    public const int DisplayNameMaxLength = 100;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public static string NormalizeUserName(string userName) =>
        userName.Trim().ToUpperInvariant();

    public static string? ValidateUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "Username is required.";
        }

        var trimmed = userName.Trim();

        if (trimmed.Length is < UserNameMinLength or > UserNameMaxLength)
        {
            return $"Username must be {UserNameMinLength}-{UserNameMaxLength} characters.";
        }

        if (!trimmed.All(IsValidUserNameCharacter))
        {
            return "Username may contain only letters, digits, '.', '_' and '-'.";
        }

        return null;
    }

    public static string? ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Display name is required.";
        }

        return displayName.Trim().Length > DisplayNameMaxLength
            ? $"Display name must be at most {DisplayNameMaxLength} characters."
            : null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (password is null || password.Length is < PasswordMinLength or > PasswordMaxLength)
        {
            return $"Password must be {PasswordMinLength}-{PasswordMaxLength} characters.";
        }

        return null;
    }

    private static bool IsValidUserNameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
}
