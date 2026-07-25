namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Produces stable, model-safe function names for configured <see cref="AIToolInstance"/> entries so
/// that multiple instances of the same definition are exposed to the AI model as distinct functions.
/// </summary>
public static class AIToolInstanceNaming
{
    private const int _maxLength = 64;

    /// <summary>
    /// Builds the unique function name presented to the AI model for the supplied instance. The name
    /// combines the definition name (<see cref="AIToolInstance.Source"/>) with the instance identifier
    /// and is sanitized to the characters allowed by chat-completion providers (letters, digits,
    /// underscores, and hyphens), truncated to 64 characters.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>A deterministic, provider-safe function name.</returns>
    public static string GetFunctionName(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var source = Sanitize(instance.Source);
        var itemId = Sanitize(instance.ItemId);
        var name = string.IsNullOrEmpty(source)
            ? itemId
            : $"{source}_{itemId}";

        if (string.IsNullOrEmpty(name))
        {
            name = "tool_instance";
        }

        if (name.Length > _maxLength)
        {
            name = name[.._maxLength];
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
