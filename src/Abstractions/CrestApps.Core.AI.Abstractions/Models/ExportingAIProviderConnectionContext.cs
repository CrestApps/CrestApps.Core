using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Models;

public sealed class ExportingAIProviderConnectionContext
{
    public AIProviderConnection Connection { get; }

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
