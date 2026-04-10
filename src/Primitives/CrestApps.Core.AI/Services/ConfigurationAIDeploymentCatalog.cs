using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;
/// <summary>
/// Decorates a persisted AI deployment store with configuration-backed deployments from appsettings.json.
/// Read operations return the merged result while write operations continue to target the persisted store only.
/// </summary>
public sealed class ConfigurationAIDeploymentCatalog : IAIDeploymentStore
{
    private readonly INamedSourceCatalog<AIDeployment> _deploymentCatalog;
    private readonly IConfiguration _configuration;
    private readonly AIOptions _aiOptions;
    private readonly AIDeploymentCatalogOptions _catalogOptions;
    private readonly ILogger _logger;

    public ConfigurationAIDeploymentCatalog(
        INamedSourceCatalog<AIDeployment> deploymentCatalog,
        IConfiguration configuration,
        IOptions<AIOptions> aiOptions,
        IOptions<AIDeploymentCatalogOptions> catalogOptions,
        ILogger<ConfigurationAIDeploymentCatalog> logger)
    {
        _deploymentCatalog = deploymentCatalog;
        _configuration = configuration;
        _aiOptions = aiOptions.Value;
        _catalogOptions = catalogOptions.Value;
        _logger = logger;
    }

    public async ValueTask<AIDeployment> FindByIdAsync(string id)
    {
        var result = await _deploymentCatalog.FindByIdAsync(id);
        if (result != null)
        {
            return result;
        }

        return (await GetConfigDeploymentsAsync(await _deploymentCatalog.GetAllAsync()))
            .FirstOrDefault(deployment => string.Equals(deployment.ItemId, id, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public async ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync()
    {
        var dbRecords = await _deploymentCatalog.GetAllAsync();
        var configRecords = await GetConfigDeploymentsAsync(dbRecords);
        if (configRecords.Count == 0)
        {
            return dbRecords;
        }

        return Merge(dbRecords, configRecords);
    }

    public async ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids)
    {
        var dbRecords = await _deploymentCatalog.GetAsync(ids);
        var requestedIds = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foundIds = dbRecords.Select(static deployment => deployment.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingIds = requestedIds.Except(foundIds).ToList();
        if (missingIds.Count == 0)
        {
            return dbRecords;
        }

        var configMatches = (await GetConfigDeploymentsAsync(dbRecords)).Where(deployment => missingIds.Contains(deployment.ItemId)).ToList();
        if (configMatches.Count == 0)
        {
            return dbRecords;
        }

        return Merge(dbRecords, configMatches);
    }

    public async ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var configRecords = await GetConfigDeploymentsAsync(await _deploymentCatalog.GetAllAsync());
        if (configRecords.Count == 0)
        {
            return await _deploymentCatalog.PageAsync(page, pageSize, context);
        }

        var allRecords = await GetAllAsync();
        var filtered = ApplyFilters(context, allRecords);
        var skip = (page - 1) * pageSize;
        return new PageResult<AIDeployment>
        {
            Count = filtered.Count(),
            Entries = filtered.Skip(skip).Take(pageSize).ToArray(),
        };
    }

    public async ValueTask<AIDeployment> FindByNameAsync(string name)
    {
        var result = await _deploymentCatalog.FindByNameAsync(name);
        if (result != null)
        {
            return result;
        }

        return (await GetConfigDeploymentsAsync(await _deploymentCatalog.GetAllAsync()))
            .FirstOrDefault(deployment => string.Equals(deployment.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public async ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(string source)
    {
        var dbRecords = await _deploymentCatalog.GetAsync(source);
        var configMatches = (await GetConfigDeploymentsAsync(dbRecords)).Where(deployment => string.Equals(deployment.Source, source, StringComparison.OrdinalIgnoreCase)).ToList();
        if (configMatches.Count == 0)
        {
            return dbRecords;
        }

        return Merge(dbRecords, configMatches);
    }

    public async ValueTask<AIDeployment> GetAsync(string name, string source)
    {
        var result = await _deploymentCatalog.GetAsync(name, source);
        if (result != null)
        {
            return result;
        }

        return (await GetConfigDeploymentsAsync(await _deploymentCatalog.GetAllAsync()))
            .FirstOrDefault(deployment =>
                string.Equals(deployment.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(deployment.Source, source, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public ValueTask<bool> DeleteAsync(AIDeployment entry) => _deploymentCatalog.DeleteAsync(entry);
    public ValueTask CreateAsync(AIDeployment entry) => _deploymentCatalog.CreateAsync(entry);
    public ValueTask UpdateAsync(AIDeployment entry) => _deploymentCatalog.UpdateAsync(entry);

    private async Task<IReadOnlyCollection<AIDeployment>> GetConfigDeploymentsAsync(IReadOnlyCollection<AIDeployment> storedDeployments)
    {
        var deployments = new Dictionary<string, AIDeployment>(StringComparer.OrdinalIgnoreCase);
        var names = storedDeployments
            .Where(static deployment => !string.IsNullOrWhiteSpace(deployment.Name))
            .ToDictionary(static deployment => deployment.Name, static deployment => deployment.ItemId, StringComparer.OrdinalIgnoreCase);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Evaluating AI deployment configuration. Stored deployments: {StoredDeploymentCount}. Deployment sections: [{DeploymentSections}]",
                storedDeployments.Count,
                string.Join(", ", _catalogOptions.DeploymentSections));
        }

        try
        {
            ReadConfiguredDeployments(deployments, names);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading AI deployment configuration.");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Finished evaluating AI deployment configuration. Config-backed deployments discovered: {ConfiguredDeploymentCount}.",
                deployments.Count);
        }

        return deployments.Values.ToArray();
    }

    private void ReadConfiguredDeployments(Dictionary<string, AIDeployment> deployments, Dictionary<string, string> names)
    {
        foreach (var sectionPath in _catalogOptions.DeploymentSections)
        {
            var section = _configuration.GetSection(sectionPath);
            var children = section.GetChildren().ToArray();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Inspecting AI deployment section '{SectionPath}'. Exists: {SectionExists}. Child count: {ChildCount}. Child keys: [{ChildKeys}].",
                    sectionPath,
                    section.Exists(),
                    children.Length,
                    string.Join(", ", children.Select(static child => child.Key)));
            }

            if (!section.Exists())
            {
                continue;
            }

            var deploymentsNode = ReadConfigurationNode(section);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Resolved AI deployment section '{SectionPath}' as {NodeType}.",
                    sectionPath,
                    GetNodeTypeName(deploymentsNode));
            }

            switch (deploymentsNode)
            {
                case JsonArray deploymentArray:
                    ReadConfiguredDeploymentsFromArray(deploymentArray, deployments, names, sectionPath);
                    break;
                case JsonObject deploymentObject:
                    ReadConfiguredDeploymentsFromObject(deploymentObject, deployments, names, sectionPath);
                    break;
                case null:
                    break;
                default:
                    _logger.LogWarning("The AI deployments configuration at '{SectionPath}' must be either an array or an object.", sectionPath);
                    break;
            }
        }
    }

    private void ReadConfiguredDeploymentsFromArray(JsonArray deploymentArray, Dictionary<string, AIDeployment> deployments, Dictionary<string, string> names, string sectionPath)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Reading {DeploymentCount} deployment entries from array section '{SectionPath}'.",
                deploymentArray.Count,
                sectionPath);
        }

        foreach (var deploymentNode in deploymentArray)
        {
            if (deploymentNode is not JsonObject deploymentObject)
            {
                _logger.LogWarning("An AI deployment entry is not a valid object. Skipping.");
                continue;
            }

            var deployment = CreateConfiguredDeployment(ParseConfiguredDeploymentEntry(deploymentObject));
            AddDeployment(deployments, names, deployment, sectionPath);
        }
    }

    private void ReadConfiguredDeploymentsFromObject(JsonObject deploymentObject, Dictionary<string, AIDeployment> deployments, Dictionary<string, string> names, string sectionPath)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Reading provider-grouped deployment entries from section '{SectionPath}'. Providers: [{ProviderNames}].",
                sectionPath,
                string.Join(", ", deploymentObject.Select(static pair => pair.Key)));
        }

        foreach (var (clientName, providerDeploymentsNode) in deploymentObject)
        {
            if (providerDeploymentsNode is not JsonArray providerDeployments)
            {
                _logger.LogWarning("The provider '{ProviderName}' must contain an array of deployments. Skipping.", clientName);
                continue;
            }

            foreach (var deploymentNode in providerDeployments)
            {
                if (deploymentNode is not JsonObject standaloneDeploymentObject)
                {
                    _logger.LogWarning("An AI deployment entry for client '{ClientName}' is not a valid object. Skipping.", clientName);
                    continue;
                }

                var deployment = CreateConfiguredDeployment(ParseConfiguredDeploymentEntry(standaloneDeploymentObject, clientName));
                AddDeployment(deployments, names, deployment, $"{sectionPath}:{clientName}");
            }
        }
    }

    private static AIDeploymentConfigurationEntry ParseConfiguredDeploymentEntry(JsonObject deploymentObject, string clientName = null)
    {
        var entry = new AIDeploymentConfigurationEntry
        {
            ClientName = deploymentObject["ClientName"].GetStringValue() ?? clientName,
            Name = deploymentObject["Name"].GetStringValue(),
            ModelName = deploymentObject["ModelName"].GetStringValue() ?? deploymentObject["Name"].GetStringValue(),
            ConnectionName = deploymentObject["ConnectionName"].GetStringValue(),
            Properties = BuildDeploymentProperties(deploymentObject),
        };
        if (TryGetDeploymentType(deploymentObject["Type"], out var deploymentType))
        {
            entry.Type = deploymentType;
        }

        return entry;
    }

    private AIDeployment CreateConfiguredDeployment(AIDeploymentConfigurationEntry entry)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Parsed AI deployment configuration entry. Provider: {ProviderName}. Name: {DeploymentName}. Model: {ModelName}. Type: {DeploymentType}. Property count: {PropertyCount}.",
                entry.ClientName,
                entry.Name,
                entry.ModelName,
                entry.Type,
                entry.Properties?.Count ?? 0);
        }

        if (string.IsNullOrWhiteSpace(entry.ClientName))
        {
            _logger.LogWarning("An AI deployment entry is missing a ClientName. Skipping.");
            return null;
        }

        if (!_aiOptions.Deployments.ContainsKey(entry.ClientName))
        {
            _logger.LogWarning("Unknown deployment provider '{ProviderName}' in AI deployment configuration. Skipping.", entry.ClientName);
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            _logger.LogWarning("A deployment entry for provider '{ProviderName}' is missing a Name. Skipping.", entry.ClientName);
            return null;
        }

        if (!entry.Type.IsValidSelection())
        {
            _logger.LogWarning("Deployment entry '{Name}' for provider '{ProviderName}' has an invalid Type. Skipping.", entry.Name, entry.ClientName);
            return null;
        }

        return new AIDeployment
        {
            ItemId = AIConfigurationRecordIds.CreateDeploymentId(entry.ClientName, entry.ConnectionName, entry.Name),
            Name = entry.Name,
            ModelName = entry.ModelName,
            Source = entry.ClientName,
            ConnectionName = entry.ConnectionName,
            Type = entry.Type,
            Properties = entry.Properties?.Count > 0 ? JsonSerializer.Deserialize<Dictionary<string, object>>(entry.Properties.DeepClone()) : null,
        };
    }

    private static JsonNode ReadConfigurationNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return section.Value is null ? null : JsonValue.Create(ParseScalar(section.Value));
        }

        if (children.All(static child => int.TryParse(child.Key, out _)))
        {
            var array = new JsonArray();
            foreach (var child in children.OrderBy(static child => int.Parse(child.Key)))
            {
                array.Add(ReadConfigurationNode(child));
            }

            return array;
        }

        var result = new JsonObject();
        foreach (var child in children)
        {
            result[child.Key] = ReadConfigurationNode(child);
        }

        return result;
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

    private static bool TryGetDeploymentType(JsonNode typeNode, out AIDeploymentType type)
    {
        type = AIDeploymentType.None;
        if (typeNode is null)
        {
            return false;
        }

        if (typeNode is JsonArray array)
        {
            foreach (var item in array)
            {
                var typeName = item.GetStringValue();
                if (string.IsNullOrWhiteSpace(typeName) || !Enum.TryParse<AIDeploymentType>(typeName, ignoreCase: true, out var parsedType) || parsedType == AIDeploymentType.None)
                {
                    type = AIDeploymentType.None;
                    return false;
                }

                type |= parsedType;
            }

            return type.IsValidSelection();
        }

        var singleTypeName = typeNode.GetStringValue();
        return !string.IsNullOrWhiteSpace(singleTypeName) && Enum.TryParse(singleTypeName, ignoreCase: true, out type) && type.IsValidSelection();
    }

    private static JsonObject BuildDeploymentProperties(JsonObject deploymentObject)
    {
        JsonObject properties = null;
        if (deploymentObject["Properties"] is JsonObject explicitProperties)
        {
            properties = (JsonObject)explicitProperties.DeepClone();
        }

        foreach (var (key, value) in deploymentObject)
        {
            properties ??= [];
            properties[key] = value?.DeepClone();
        }

        return properties;
    }

    private void AddDeployment(
        Dictionary<string, AIDeployment> deployments,
        Dictionary<string, string> names,
        AIDeployment deployment,
        string sourceDescription)
    {
        if (deployment == null)
        {
            return;
        }

        if (names.TryGetValue(deployment.Name, out var existingItemId) &&
            !string.Equals(existingItemId, deployment.ItemId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Skipping AI deployment '{DeploymentName}' from {SourceDescription} because another deployment with the same name is already defined.",
                deployment.Name,
                sourceDescription);
            return;
        }

        names[deployment.Name] = deployment.ItemId;
        deployments[deployment.ItemId] = deployment;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Registered configuration-backed AI deployment '{DeploymentName}' from '{SourceDescription}' with item id '{DeploymentId}' and source '{DeploymentSource}'.",
                deployment.Name,
                sourceDescription,
                deployment.ItemId,
                deployment.Source);
        }
    }

    private static string GetNodeTypeName(JsonNode node)
    {
        return node switch
        {
            null => "null",
            JsonArray => "array",
            JsonObject => "object",
            JsonValue => "scalar",
            _ => node.GetType().Name,
        };
    }

    private static List<AIDeployment> Merge(IReadOnlyCollection<AIDeployment> primary, IReadOnlyCollection<AIDeployment> secondary)
    {
        var merged = new List<AIDeployment>(primary.Count + secondary.Count);
        merged.AddRange(primary);
        merged.AddRange(secondary);
        return merged;
    }

    private static List<AIDeployment> Merge(IReadOnlyCollection<AIDeployment> primary, List<AIDeployment> secondary)
    {
        var merged = new List<AIDeployment>(primary.Count + secondary.Count);
        merged.AddRange(primary);
        merged.AddRange(secondary);
        return merged;
    }

    private static IEnumerable<AIDeployment> ApplyFilters(QueryContext context, IEnumerable<AIDeployment> records)
    {
        if (context is null)
        {
            return records;
        }

        if (!string.IsNullOrEmpty(context.Source))
        {
            records = records.Where(deployment => string.Equals(deployment.Source, context.Source, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(context.Name))
        {
            records = records.Where(deployment => deployment.Name.Contains(context.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Sorted)
        {
            records = records.OrderBy(static deployment => deployment.Name, StringComparer.OrdinalIgnoreCase);
        }

        return records;
    }
}
