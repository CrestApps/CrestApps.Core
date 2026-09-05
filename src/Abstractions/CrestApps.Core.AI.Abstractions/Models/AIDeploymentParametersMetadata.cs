namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata stored on an AI profile, profile template, or chat interaction that holds the model
/// parameter values selected by an operator. Values are keyed by the registered parameter technical
/// name so new parameters do not require model or storage changes.
/// </summary>
public sealed class AIDeploymentParametersMetadata
{
    /// <summary>
    /// Gets or sets the parameter values selected for the chat deployment, keyed by their registered
    /// technical name.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the parameter values selected for the utility deployment, keyed by their registered
    /// technical name. The utility deployment backs background completions such as title generation,
    /// data extraction, and post-session processing, so it is configured independently of the chat
    /// deployment.
    /// </summary>
    public Dictionary<string, string> UtilityValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the value selected for the given chat deployment parameter, or <see langword="null"/> when
    /// none was selected.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public string GetValue(string parameterName)
    {
        return GetValue(Values, parameterName);
    }

    /// <summary>
    /// Gets the value selected for the given utility deployment parameter, or <see langword="null"/>
    /// when none was selected.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public string GetUtilityValue(string parameterName)
    {
        return GetValue(UtilityValues, parameterName);
    }

    private static string GetValue(Dictionary<string, string> values, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName) || values is null)
        {
            return null;
        }

        return values.TryGetValue(parameterName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
