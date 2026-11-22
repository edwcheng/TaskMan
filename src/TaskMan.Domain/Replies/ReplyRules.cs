namespace TaskMan.Domain.Replies;

public static class ReplyRules
{
    public const int BodyMaxLength = 100_000;

    public static string? ValidateBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Reply body is required.";
        }

        return body.Length > BodyMaxLength
            ? $"Reply body must be at most {BodyMaxLength} characters."
            : null;
    }
}
