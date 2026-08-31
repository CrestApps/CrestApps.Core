namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata stored on an AI profile, profile template, or chat interaction that holds the model
/// parameter values selected by an operator. Values are keyed by the registered parameter technical
/// name so new parameters do not require model or storage changes.
/// </summary>
public sealed class AIModelParametersMetadata
{
    /// <summary>
    /// Gets or sets the selected parameter values keyed by their registered technical name.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the value selected for the given parameter, or <see langword="null"/> when none was selected.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public string GetValue(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName) || Values is null)
        {
            return null;
        }

        return Values.TryGetValue(parameterName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
