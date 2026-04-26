using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context passed to event handlers when an AI provider connection is being exported,
/// providing access to the connection and its serialized export payload.
/// </summary>
public sealed class ExportingAIProviderConnectionContext
{
    /// <summary>
    /// Gets the AI provider connection being exported.
    /// </summary>
    public AIProviderConnection Connection { get; }

    /// <summary>
    /// Gets the mutable JSON object that will be serialized as the export payload.
    /// </summary>
    public JsonObject ExportData { get; }

    public ExportingAIProviderConnectionContext(
        AIProviderConnection connection,
        JsonObject exportData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Connection = connection;
        ExportData = exportData ?? [];
    }
}
