namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Extension methods for <see cref="AIToolInstance"/>, including production of stable, model-safe
/// function names so that multiple instances built from the same source are exposed to the AI model as
/// distinct callable functions.
/// </summary>
public static class AIToolInstanceExtensions
{
    private const int MaxFunctionNameLength = 64;

    /// <summary>
    /// Builds the unique function name presented to the AI model for the supplied instance. The name is
    /// derived from the instance's unique <see cref="AIToolInstance.Name"/> (falling back to its
    /// identifier) and is sanitized to the characters allowed by chat-completion providers (letters,
    /// digits, underscores, and hyphens), truncated to 64 characters.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>A deterministic, provider-safe function name.</returns>
    public static string GetFunctionName(this AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var name = Sanitize(instance.Name);

        if (string.IsNullOrEmpty(name))
        {
            name = Sanitize(instance.ItemId);
        }

        if (string.IsNullOrEmpty(name))
        {
            name = "tool_instance";
        }

        if (name.Length > MaxFunctionNameLength)
        {
            name = name[..MaxFunctionNameLength];
        }

        return name;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            buffer[length++] = char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'
                ? c
                : '_';
        }

        return new string(buffer, 0, length);
    }
}
