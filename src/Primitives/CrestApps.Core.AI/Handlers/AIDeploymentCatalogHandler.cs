using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIDeploymentCatalogHandler : CatalogEntryHandlerBase<AIDeployment>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIDeploymentStore _deploymentStore;
    private readonly IAIProviderConnectionStore _connectionStore;
    private readonly AIOptions _aiOptions;

    internal readonly IStringLocalizer S;

    public AIDeploymentCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIDeploymentStore deploymentStore,
        IAIProviderConnectionStore connectionStore,
        IOptions<AIOptions> aiOptions,
        IStringLocalizer<AIDeploymentCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _deploymentStore = deploymentStore;
        _connectionStore = connectionStore;
        _aiOptions = aiOptions.Value;
        S = stringLocalizer;
    }

    public override Task InitializingAsync(InitializingContext<AIDeployment> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<AIDeployment> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task InitializedAsync(InitializedContext<AIDeployment> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<AIDeployment> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidatingContext<AIDeployment> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Deployment Name is required."], [nameof(AIDeployment.Name)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.ModelName))
        {
            context.Result.Fail(new ValidationResult(S["Model name is required."], [nameof(AIDeployment.ModelName)]));
        }

        if (!context.Model.Type.IsValidSelection())
        {
            context.Result.Fail(new ValidationResult(S["The deployment type '{0}' is not valid.", context.Model.Type], [nameof(AIDeployment.Type)]));
        }

        if (!string.IsNullOrWhiteSpace(context.Model.ClientName) && !_aiOptions.Deployments.ContainsKey(context.Model.ClientName))
        {
            context.Result.Fail(new ValidationResult(S["Invalid provider."], [nameof(AIDeployment.ClientName)]));
        }

        await ValidateUniqueNameAsync(context, cancellationToken);
        await ValidateConnectionAsync(context, cancellationToken);
    }

    private void EnsureCreatedDefaults(AIDeployment deployment)
    {
        if (deployment.CreatedUtc == default)
        {
            deployment.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        deployment.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        deployment.Author ??= user.Identity?.Name;
    }

    private async Task ValidateUniqueNameAsync(ValidatingContext<AIDeployment> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            return;
        }

        var existing = await _deploymentStore.FindByNameAsync(context.Model.Name, cancellationToken);

        if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
        {
            context.Result.Fail(new ValidationResult(S["A deployment with this name already exists. The name must be unique."], [nameof(AIDeployment.Name)]));
        }
    }

    private async Task ValidateConnectionAsync(ValidatingContext<AIDeployment> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Model.ClientName) ||
            string.IsNullOrWhiteSpace(context.Model.ConnectionName) ||
            HasContainedConnection(context.Model.ClientName))
        {
            return;
        }

        var connections = await _connectionStore.GetAsync(context.Model.ClientName, cancellationToken);

        if (connections.Count == 0)
        {
            context.Result.Fail(new ValidationResult(S["There are no configured connection for the provider: {0}", context.Model.ClientName], [nameof(AIDeployment.ClientName)]));

            return;
        }

        if (!connections.Any(connection => string.Equals(connection.Name, context.Model.ConnectionName, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result.Fail(new ValidationResult(S["Invalid connection name provided."], [nameof(AIDeployment.ConnectionName)]));
        }
    }

    private static Task PopulateAsync(AIDeployment deployment, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(AIDeployment.Name), value => deployment.Name = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDeployment.ModelName), value => deployment.ModelName = value);

        if (string.IsNullOrWhiteSpace(deployment.ModelName) && !string.IsNullOrWhiteSpace(deployment.Name))
        {
            deployment.ModelName = deployment.Name;
        }

        if (!json.TryUpdateTrimmedStringValue(nameof(AIDeployment.ClientName), value => deployment.ClientName = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            json.TryUpdateTrimmedStringValue(nameof(AIDeployment.ProviderName), value => deployment.ClientName = value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        json.TryUpdateTrimmedStringValue(nameof(AIDeployment.ConnectionName), value => deployment.ConnectionName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDeployment.OwnerId), value => deployment.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDeployment.Author), value => deployment.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIDeployment.CreatedUtc), out var createdUtc))
        {
            deployment.CreatedUtc = createdUtc;
        }

        if (TryGetDeploymentType(json, out var type))
        {
            deployment.Type = type;
        }

        MergeProperties(deployment, json);

        return Task.CompletedTask;
    }

    private static bool TryGetDeploymentType(JsonObject json, out AIDeploymentType type)
    {
        type = AIDeploymentType.None;

        if (json is null || !json.TryGetPropertyValue(nameof(AIDeployment.Type), out var typeNode) || typeNode is null)
        {
            return false;
        }

        if (typeNode is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is null ||
                    item.GetStringValue() is not { Length: > 0 } itemText ||
                    !Enum.TryParse(itemText, true, out AIDeploymentType parsedType) ||
                    parsedType == AIDeploymentType.None)
                {
                    type = AIDeploymentType.None;

                    return false;
                }

                type |= parsedType;
            }

            return type.IsValidSelection();
        }

        return typeNode.GetStringValue() is { Length: > 0 } typeText &&
            Enum.TryParse(typeText, true, out type) &&
            type.IsValidSelection();
    }

    private static void MergeProperties(AIDeployment deployment, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIDeployment.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(deployment.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        deployment.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }

    private bool HasContainedConnection(string clientName)
    {
        return !string.IsNullOrWhiteSpace(clientName) &&
            _aiOptions.Deployments.TryGetValue(clientName, out var entry) &&
            entry.UseContainedConnection;
    }
}
