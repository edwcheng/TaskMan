using System.Globalization;
using System.Text;

namespace TaskMan.Domain.Forums;

public static class ForumRules
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Forum name is required.";
        }

        return name.Trim().Length > NameMaxLength
            ? $"Forum name must be at most {NameMaxLength} characters."
            : null;
    }

    public static string? ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return "Forum slug is required.";
        }

        var trimmed = slug.Trim();

        if (trimmed.Length > SlugMaxLength)
        {
            return $"Forum slug must be at most {SlugMaxLength} characters.";
        }

        if (trimmed[0] == '-' || trimmed[^1] == '-')
        {
            return "Forum slug cannot start or end with a hyphen.";
        }

        if (trimmed.Any(c => !IsValidSlugCharacter(c)))
        {
            return "Forum slug may contain only lowercase letters, digits and hyphens.";
        }

        if (trimmed.Contains("--", StringComparison.Ordinal))
        {
            return "Forum slug cannot contain consecutive hyphens.";
        }

        return null;
    }

    public static string? ValidateDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        return description.Trim().Length > DescriptionMaxLength
            ? $"Forum description must be at most {DescriptionMaxLength} characters."
            : null;
    }

    public static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    public static string Slugify(string name)
    {
        var normalized = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasHyphen = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasHyphen = false;
            }
            else if (builder.Length > 0 && !previousWasHyphen)
            {
                builder.Append('-');
                previousWasHyphen = true;
            }

            if (builder.Length >= SlugMaxLength)
            {
                break;
            }
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length == 0 ? "forum" : slug;
    }

    private static bool IsValidSlugCharacter(char character) =>
        char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';
}
