using System.Text.RegularExpressions;

namespace CrestApps.Core.Blazor.Web.Services;

/// <summary>
/// Centralised helper that mirrors the algorithm in
/// <c>technical-name-generator.js</c> used by the MVC host.
/// </summary>
public static partial class TechnicalNameGenerator
{
    /// <summary>
    /// Converts a display name into a PascalCase technical identifier
    /// by stripping non-alphanumeric characters and title-casing each word.
    /// </summary>
    public static string Generate(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return string.Empty;
        }

        var cleaned = NonAlphanumericExceptSeparators().Replace(displayText.Trim(), "");
        var parts = cleaned.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\s\-_]")]
    private static partial Regex NonAlphanumericExceptSeparators();
}
