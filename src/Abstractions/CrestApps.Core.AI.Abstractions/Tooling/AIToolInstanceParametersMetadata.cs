namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// The user-declared parameters attached to an <see cref="AIToolInstance"/>, stored in the instance's
/// properties bag. Kept as a dedicated metadata type — rather than folded into any one source's settings
/// — so every source that opts into parameter support reads them from the same place.
/// </summary>
public sealed class AIToolInstanceParametersMetadata
{
    /// <summary>
    /// Gets or sets the declared parameters, in the order the user arranged them. Order is preserved
    /// because it drives the order properties appear in the schema the AI model sees.
    /// </summary>
    public List<AIToolInstanceParameter> Parameters { get; set; } = [];
}
