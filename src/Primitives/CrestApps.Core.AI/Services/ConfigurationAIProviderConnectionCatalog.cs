using System.Globalization;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Decorates a persisted AI provider connection store with configuration-backed
/// connections from appsettings.json. Read operations return the merged result
/// while write operations continue to target the persisted store only.
/// </summary>
public sealed class ConfigurationAIProviderConnectionCatalog : INamedSourceCatalog<AIProviderConnection>
{
    public const string PersistedCatalogKey = "PersistedCatalog";

    private readonly INamedSourceCatalog<AIProviderConnection> _inner;
    private readonly IConfiguration _configuration;
    private readonly AIProviderConnectionCatalogOptions _options;
    private readonly ILogger _logger;

    public ConfigurationAIProviderConnectionCatalog(
        [FromKeyedServices(PersistedCatalogKey)] INamedSourceCatalog<AIProviderConnection> inner,
        IConfiguration configuration,
        IOptions<AIProviderConnectionCatalogOptions> options,
        ILogger<ConfigurationAIProviderConnectionCatalog> logger)
    {
        _inner = inner;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<AIProviderConnection> FindByIdAsync(string id)
    {
        var result = await _inner.FindByIdAsync(id);
        if (result != null)
        {
            return result;
        }

        return (await GetConfiguredConnectionsAsync(await _inner.GetAllAsync()))
            .FirstOrDefault(connection => string.Equals(connection.ItemId, id, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public async ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAllAsync()
    {
        var storedConnections = await _inner.GetAllAsync();
        var configuredConnections = await GetConfiguredConnectionsAsync(storedConnections);

        if (configuredConnections.Count == 0)
        {
            return storedConnections;
        }

        var merged = new List<AIProviderConnection>(storedConnections.Count + configuredConnections.Count);
        merged.AddRange(storedConnections);
        merged.AddRange(configuredConnections);

        return merged;
    }

    public async ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAsync(IEnumerable<string> ids)
    {
        var storedConnections = await _inner.GetAsync(ids);
        var requestedIds = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foundIds = storedConnections.Select(static connection => connection.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingIds = requestedIds.Except(foundIds).ToList();

        if (missingIds.Count == 0)
        {
            return storedConnections;
        }

        var configuredConnections = (await GetConfiguredConnectionsAsync(storedConnections))
            .Where(connection => missingIds.Contains(connection.ItemId))
            .ToArray();

        if (configuredConnections.Length == 0)
        {
            return storedConnections;
        }

        var merged = new List<AIProviderConnection>(storedConnections.Count + configuredConnections.Length);
        merged.AddRange(storedConnections);
        merged.AddRange(configuredConnections);

        return merged;
    }

    public async ValueTask<PageResult<AIProviderConnection>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var allConnections = await GetAllAsync();
        var filtered = ApplyFilters(context, allConnections);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIProviderConnection>
        {
            Count = filtered.Count(),
            Entries = filtered.Skip(skip).Take(pageSize).ToArray(),
        };
    }

    public async ValueTask<AIProviderConnection> FindByNameAsync(string name)
    {
        var result = await _inner.FindByNameAsync(name);
        if (result != null)
        {
            return result;
        }

        return (await GetConfiguredConnectionsAsync(await _inner.GetAllAsync()))
            .FirstOrDefault(connection => string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public async ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAsync(string source)
    {
        var storedConnections = await _inner.GetAsync(source);
        var configuredConnections = (await GetConfiguredConnectionsAsync(storedConnections))
            .Where(connection => string.Equals(connection.Source, source, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (configuredConnections.Length == 0)
        {
            return storedConnections;
        }

        var merged = new List<AIProviderConnection>(storedConnections.Count + configuredConnections.Length);
        merged.AddRange(storedConnections);
        merged.AddRange(configuredConnections);

        return merged;
    }

    public async ValueTask<AIProviderConnection> GetAsync(string name, string source)
    {
        var result = await _inner.GetAsync(name, source);
        if (result != null)
        {
            return result;
        }

        return (await GetConfiguredConnectionsAsync(await _inner.GetAllAsync()))
            .FirstOrDefault(connection =>
                string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(connection.Source, source, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public ValueTask<bool> DeleteAsync(AIProviderConnection entry) => _inner.DeleteAsync(entry);

    public ValueTask CreateAsync(AIProviderConnection entry) => _inner.CreateAsync(entry);

    public ValueTask UpdateAsync(AIProviderConnection entry) => _inner.UpdateAsync(entry);

    private Task<IReadOnlyCollection<AIProviderConnection>> GetConfiguredConnectionsAsync(IReadOnlyCollection<AIProviderConnection> storedConnections)
    {
        var connections = new Dictionary<string, AIProviderConnection>(StringComparer.OrdinalIgnoreCase);
        var names = storedConnections
            .Where(static connection => !string.IsNullOrWhiteSpace(connection.Name))
            .ToDictionary(static connection => connection.Name, static connection => connection.ItemId, StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var sectionPath in _options.ConnectionSections)
            {
                ReadTopLevelConnections(sectionPath, connections, names);
            }

            foreach (var sectionPath in _options.ProviderSections)
            {
                ReadProviderConnections(sectionPath, connections, names);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading AI provider connection configuration.");
        }

        return Task.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.Values.ToArray());
    }

    private void ReadTopLevelConnections(string sectionPath, Dictionary<string, AIProviderConnection> connections, Dictionary<string, string> names)
    {
        var section = _configuration.GetSection(sectionPath);
        if (!section.Exists())
        {
            return;
        }

        foreach (var connectionSection in section.GetChildren())
        {
            var values = ReadObject(connectionSection);
            var connection = ParseConnection(values, fallbackName: connectionSection.Key);
            AddConfiguredConnection(connections, names, connection, sectionPath);
        }
    }

    private void ReadProviderConnections(string sectionPath, Dictionary<string, AIProviderConnection> connections, Dictionary<string, string> names)
    {
        var section = _configuration.GetSection(sectionPath);
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var providerName = AIProviderNameNormalizer.Normalize(providerSection.Key);
            var connectionsSection = providerSection.GetSection("Connections");
            if (!connectionsSection.Exists())
            {
                continue;
            }

            foreach (var connectionSection in connectionsSection.GetChildren())
            {
                var values = ReadObject(connectionSection);
                var connection = ParseConnection(values, fallbackName: connectionSection.Key, providerName: providerName);
                AddConfiguredConnection(connections, names, connection, $"{sectionPath}:{providerSection.Key}:Connections:{connectionSection.Key}");
            }
        }
    }

    private void AddConfiguredConnection(
        Dictionary<string, AIProviderConnection> connections,
        Dictionary<string, string> names,
        AIProviderConnection connection,
        string sourceDescription)
    {
        if (connection == null)
        {
            return;
        }

        if (names.TryGetValue(connection.Name, out var existingItemId) &&
            !string.Equals(existingItemId, connection.ItemId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Skipping AI connection '{ConnectionName}' from '{SourceDescription}' because another connection with the same name is already defined.",
                connection.Name,
                sourceDescription);
            return;
        }

        names[connection.Name] = connection.ItemId;
        connections[connection.ItemId] = connection;
    }

    private AIProviderConnection ParseConnection(
        Dictionary<string, object> values,
        string fallbackName = null,
        string providerName = null)
    {
        var connectionName = values.GetStringValue("Name", false) ?? fallbackName;
        var clientName = AIProviderNameNormalizer.Normalize(
            values.GetStringValue("ClientName", false) ??
            values.GetStringValue("ProviderName", false) ??
            providerName);

        if (string.IsNullOrWhiteSpace(connectionName))
        {
            _logger.LogWarning("An AI connection configuration entry is missing the required Name value and will be ignored.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            _logger.LogWarning("The AI connection '{ConnectionName}' is missing the required ClientName value and will be ignored.", connectionName);
            return null;
        }

        var displayText = values.GetStringValue("DisplayText", false);
        var properties = values
            .Where(static pair => !IsConnectionMetadataKey(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return new AIProviderConnection
        {
            ItemId = AIConfigurationRecordIds.CreateConnectionId(clientName, connectionName),
            Name = connectionName,
            DisplayText = string.IsNullOrWhiteSpace(displayText) ? connectionName : displayText,
            ClientName = clientName,
            Properties = properties.Count > 0 ? properties : null,
        };
    }

    private static bool IsConnectionMetadataKey(string key)
    {
        return string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "ClientName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "ProviderName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "DisplayText", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "ConnectionNameAlias", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "ChatDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "DefaultChatDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "DefaultDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "EmbeddingDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "ImagesDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "UtilityDeploymentName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "SpeechToTextDeploymentName", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> ReadObject(IConfigurationSection section)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in section.GetChildren())
        {
            values[child.Key] = ReadValue(child);
        }

        return values;
    }

    private static object ReadValue(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();

        if (children.Length == 0)
        {
            return ParseScalar(section.Value);
        }

        if (children.All(static child => int.TryParse(child.Key, out _)))
        {
            return children
                .OrderBy(static child => int.Parse(child.Key, CultureInfo.InvariantCulture))
                .Select(ReadValue)
                .ToArray();
        }

        return children.ToDictionary(static child => child.Key, ReadValue, StringComparer.OrdinalIgnoreCase);
    }

    private static object ParseScalar(string value)
    {
        if (bool.TryParse(value, out var booleanValue))
        {
            return booleanValue;
        }

        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(value, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, out var doubleValue))
        {
            return doubleValue;
        }

        return value;
    }

    private static IEnumerable<AIProviderConnection> ApplyFilters(QueryContext context, IEnumerable<AIProviderConnection> records)
    {
        if (context is null)
        {
            return records;
        }

        if (!string.IsNullOrEmpty(context.Source))
        {
            records = records.Where(connection => string.Equals(connection.Source, context.Source, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(context.Name))
        {
            records = records.Where(connection => connection.Name.Contains(context.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Sorted)
        {
            records = records.OrderBy(static connection => connection.DisplayText ?? connection.Name, StringComparer.OrdinalIgnoreCase);
        }

        return records;
    }
}
