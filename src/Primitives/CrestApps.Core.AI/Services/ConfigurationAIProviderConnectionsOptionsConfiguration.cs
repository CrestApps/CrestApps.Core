using System.Globalization;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

internal sealed class ConfigurationAIProviderConnectionsOptionsConfiguration : IPostConfigureOptions<AIProviderOptions>
{
    private readonly IConfiguration _configuration;
    private readonly AIProviderConnectionCatalogOptions _catalogOptions;
    private readonly ILogger _logger;

    public ConfigurationAIProviderConnectionsOptionsConfiguration(
        IConfiguration configuration,
        IOptions<AIProviderConnectionCatalogOptions> catalogOptions,
        ILogger<ConfigurationAIProviderConnectionsOptionsConfiguration> logger)
    {
        _configuration = configuration;
        _catalogOptions = catalogOptions.Value;
        _logger = logger;
    }

    public void PostConfigure(string name, AIProviderOptions options)
    {
        foreach (var sectionPath in _catalogOptions.ConnectionSections)
        {
            ReadTopLevelConnections(options, sectionPath);
        }

        foreach (var sectionPath in _catalogOptions.ProviderSections)
        {
            ReadProviderConnections(options, sectionPath);
        }
    }

    private void ReadTopLevelConnections(AIProviderOptions options, string sectionPath)
    {
        var section = _configuration.GetSection(sectionPath);
        if (!section.Exists())
        {
            return;
        }

        foreach (var connectionSection in section.GetChildren())
        {
            try
            {
                var connection = ReadConnection(connectionSection);
                MergeConnection(options, connection, $"{sectionPath}:{connectionSection.Key}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to bind AI connection entry from '{SectionPath}:{ConnectionKey}'.",
                    sectionPath,
                    connectionSection.Key);
            }
        }
    }

    private void ReadProviderConnections(AIProviderOptions options, string sectionPath)
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
                try
                {
                    var connection = ReadConnection(connectionSection, providerName);
                    MergeConnection(options, connection, $"{sectionPath}:{providerSection.Key}:Connections:{connectionSection.Key}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to bind AI connection entry from '{SectionPath}:{ProviderKey}:Connections:{ConnectionKey}'.",
                        sectionPath,
                        providerSection.Key,
                        connectionSection.Key);
                }
            }
        }
    }

    private void MergeConnection(AIProviderOptions options, AIProviderConnectionEntry connection, string sourcePath)
    {
        var connectionName = connection.GetStringValue("Name", false);
        var clientName = AIProviderNameNormalizer.Normalize(connection.GetStringValue("ClientName", false));

        if (string.IsNullOrWhiteSpace(connectionName))
        {
            _logger.LogWarning(
                "The AI connection entry at '{SourcePath}' is missing the required 'Name' property and will be ignored.",
                sourcePath);

            return;
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            _logger.LogWarning(
                "The AI connection '{ConnectionName}' at '{SourcePath}' is missing the required 'ClientName' property and will be ignored.",
                connectionName,
                sourcePath);

            return;
        }

        AIProviderOptionsConnectionMerger.MergeConnection(options, clientName, connectionName, connection);
    }

    private static AIProviderConnectionEntry ReadConnection(IConfigurationSection section, string providerName = null)
    {
        var values = ReadObject(section);
        if (!values.ContainsKey("Name") && !int.TryParse(section.Key, out _))
        {
            values["Name"] = section.Key;
        }

        if (!string.IsNullOrWhiteSpace(providerName) && !values.ContainsKey("ClientName") && !values.ContainsKey("ProviderName"))
        {
            values["ClientName"] = providerName;
        }

        var displayText = values.GetStringValue("DisplayText", false) ??
            values.GetStringValue("Name", false) ??
            section.Key;

        values["DisplayText"] = displayText;

        return new AIProviderConnectionEntry(values);
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

        return value;
    }
}
