using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;


/// <summary>
/// Holds the MVC sample's runtime projection of stored AI provider connections.
/// The snapshot is loaded during startup and refreshed after connection changes
/// so <see cref="AIProviderOptions"/> can be rebuilt without querying YesSql
/// inside the options pipeline.
/// </summary>
public sealed class MvcAIProviderOptionsStore
{
    private readonly object _syncLock = new();

    private Dictionary<string, AIProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<AIProviderConnection> connections)
    {
        var providers = new Dictionary<string, AIProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in connections.GroupBy(static connection => AIProviderNameNormalizer.Normalize(connection.ClientName)))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                continue;
            }

            var provider = new AIProvider
            {
                Connections = new Dictionary<string, AIProviderConnectionEntry>(StringComparer.OrdinalIgnoreCase),
            };

            foreach (var connection in group)
            {
                if (string.IsNullOrWhiteSpace(connection.Name))
                {
                    continue;
                }

                var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (connection.Properties is not null)
                {
                    foreach (var property in connection.Properties)
                    {
                        values[property.Key] = property.Value;
                    }
                }

                values["ConnectionNameAlias"] = connection.Name;

                provider.Connections[connection.Name] = new AIProviderConnectionEntry(values);
            }

            if (provider.Connections.Count == 0)
            {
                continue;
            }

            providers[group.Key] = provider;
        }

        lock (_syncLock)
        {
            _providers = providers;
        }
    }

    public void ApplyTo(AIProviderOptions options)
    {
        Dictionary<string, AIProvider> providers;

        lock (_syncLock)
        {
            providers = new Dictionary<string, AIProvider>(_providers, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var provider in providers)
        {
            var targetProvider = AIProviderOptionsConnectionMerger.GetOrAddProvider(options, provider.Key);

            foreach (var connection in provider.Value.Connections)
            {
                AIProviderOptionsConnectionMerger.MergeConnection(targetProvider, connection.Key, connection.Value);
            }
        }
    }
}
