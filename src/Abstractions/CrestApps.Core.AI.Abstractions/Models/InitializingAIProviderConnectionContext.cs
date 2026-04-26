namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context passed to event handlers when an AI provider connection is being initialized,
/// allowing handlers to inject or transform the connection's runtime values.
/// </summary>
public sealed class InitializingAIProviderConnectionContext
{
    /// <summary>
    /// Gets a mutable dictionary of runtime values that will be merged into the connection configuration.
    /// </summary>
    public Dictionary<string, object> Values { get; } = [];

    /// <summary>
    /// Gets the AI provider connection being initialized.
    /// </summary>
    public AIProviderConnection Connection { get; }

    public InitializingAIProviderConnectionContext(AIProviderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Connection = connection;
    }
}
