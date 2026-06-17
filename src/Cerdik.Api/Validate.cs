using System.Text.RegularExpressions;

namespace Cerdik.Api;

/// <summary>Minimal request guards that throw the { error, code } envelope on failure.</summary>
internal static partial class Validate
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public static void Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex().IsMatch(email))
        {
            throw ApiException.BadRequest("A valid email is required.", "invalid_email");
        }
    }

    public static void Password(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw ApiException.BadRequest("Password must be at least 8 characters.", "weak_password");
        }
    }

    public static void NotEmpty(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiException.BadRequest($"{field} is required.", "required");
        }
    }
}
