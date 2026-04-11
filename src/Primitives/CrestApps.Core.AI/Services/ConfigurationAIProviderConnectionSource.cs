using System.Globalization;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// A read-only catalog source that reads AI provider connections from application
/// configuration (e.g., appsettings.json). Registered with Order = 100 so that
/// DB-backed sources (Order = 0) take precedence when entries share the same name.
/// </summary>
public sealed class ConfigurationAIProviderConnectionSource : INamedSourceCatalogSource<AIProviderConnection>
{
    private readonly IConfiguration _configuration;
    private readonly AIProviderConnectionCatalogOptions _options;
    private readonly ILogger _logger;

    public ConfigurationAIProviderConnectionSource(
        IConfiguration configuration,
        IOptions<AIProviderConnectionCatalogOptions> options,
        ILogger<ConfigurationAIProviderConnectionSource> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public int Order => 100;

    public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetEntriesAsync(IReadOnlyCollection<AIProviderConnection> knownEntries)
    {
        var connections = new Dictionary<string, AIProviderConnection>(StringComparer.OrdinalIgnoreCase);
        var names = knownEntries
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

        return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.Values);
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
}
