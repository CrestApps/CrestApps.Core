namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Produces stable, model-safe function names for configured <see cref="AIToolDefinition"/> entries so
/// that multiple definitions built from the same source are exposed to the AI model as distinct functions.
/// </summary>
public static class AIToolDefinitionNaming
{
    private const int _maxLength = 64;

    /// <summary>
    /// Builds the unique function name presented to the AI model for the supplied definition. The name
    /// combines the source name (<see cref="AIToolDefinition.Source"/>) with the definition identifier
    /// and is sanitized to the characters allowed by chat-completion providers (letters, digits,
    /// underscores, and hyphens), truncated to 64 characters.
    /// </summary>
    /// <param name="definition">The configured tool definition.</param>
    /// <returns>A deterministic, provider-safe function name.</returns>
    public static string GetFunctionName(AIToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var source = Sanitize(definition.Source);
        var itemId = Sanitize(definition.ItemId);
        var name = string.IsNullOrEmpty(source)
            ? itemId
            : $"{source}_{itemId}";

        if (string.IsNullOrEmpty(name))
        {
            name = "tool_definition";
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
