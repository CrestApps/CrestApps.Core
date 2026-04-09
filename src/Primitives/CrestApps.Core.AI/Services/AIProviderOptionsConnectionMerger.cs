using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;

namespace CrestApps.Core.AI.Services;

public static class AIProviderOptionsConnectionMerger
{
    public static AIProvider GetOrAddProvider(AIProviderOptions options, string providerName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(providerName);

        if (!options.Providers.TryGetValue(providerName, out var provider))
        {
            provider = new AIProvider
            {
                Connections = new Dictionary<string, AIProviderConnectionEntry>(StringComparer.OrdinalIgnoreCase),
            };

            options.Providers[providerName] = provider;
        }
        else
        {
            provider.Connections ??= new Dictionary<string, AIProviderConnectionEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return provider;
    }

    public static bool MergeConnection(
        AIProviderOptions options,
        string providerName,
        string connectionName,
        AIProviderConnectionEntry connection,
        bool overwriteExisting = false)
    {
        var provider = GetOrAddProvider(options, providerName);

        return MergeConnection(provider, connectionName, connection, overwriteExisting);
    }

    public static bool MergeConnection(
        AIProvider provider,
        string connectionName,
        AIProviderConnectionEntry connection,
        bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        ArgumentNullException.ThrowIfNull(connection);

        provider.Connections ??= new Dictionary<string, AIProviderConnectionEntry>(StringComparer.OrdinalIgnoreCase);

        if (!overwriteExisting && provider.Connections.ContainsKey(connectionName))
        {
            return false;
        }

        var normalizedConnection = NormalizeConnection(connectionName, connection);
        provider.Connections[connectionName] = normalizedConnection;

        return true;
    }

    private static AIProviderConnectionEntry NormalizeConnection(string connectionName, AIProviderConnectionEntry connection)
    {
        var values = new Dictionary<string, object>(connection, StringComparer.OrdinalIgnoreCase);

        var displayText = values.GetStringValue("DisplayText", false);

        if (string.IsNullOrWhiteSpace(displayText))
        {
            values["DisplayText"] = connectionName;
        }
        else
        {
            values["DisplayText"] = displayText;
        }

        return new AIProviderConnectionEntry(values);
    }
}
