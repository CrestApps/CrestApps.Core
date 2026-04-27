using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIProviderConnectionCatalogHandler : CatalogEntryHandlerBase<AIProviderConnection>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly AIOptions _aiOptions;
    private readonly IAIProviderConnectionStore _store;

    internal readonly IStringLocalizer S;

    public AIProviderConnectionCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IOptions<AIOptions> aiOptions,
        IAIProviderConnectionStore store,
        IStringLocalizer<AIProviderConnectionCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _aiOptions = aiOptions.Value;
        _store = store;
        S = stringLocalizer;
    }

    public override Task InitializingAsync(InitializingContext<AIProviderConnection> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, true);

    public override Task UpdatingAsync(UpdatingContext<AIProviderConnection> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, false);

    public override Task InitializedAsync(InitializedContext<AIProviderConnection> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<AIProviderConnection> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidatingContext<AIProviderConnection> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Profile Name is required."], [nameof(AIProviderConnection.Name)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(S["Source is required."], [nameof(AIProviderConnection.Source)]));
        }
        else if (!_aiOptions.ConnectionSources.ContainsKey(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(S["Invalid source."], [nameof(AIProviderConnection.Source)]));
        }

        await ValidateUniqueNameAsync(context, cancellationToken);
    }

    private void EnsureCreatedDefaults(AIProviderConnection connection)
    {
        if (connection.CreatedUtc == default)
        {
            connection.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        connection.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        connection.Author ??= user.Identity?.Name;
    }

    private async Task ValidateUniqueNameAsync(ValidatingContext<AIProviderConnection> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            return;
        }

        var existing = await _store.FindByNameAsync(context.Model.Name, cancellationToken);

        if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
        {
            context.Result.Fail(new ValidationResult(S["A connection with this name already exists. The name must be unique."], [nameof(AIProviderConnection.Name)]));
        }
    }

    private static Task PopulateAsync(AIProviderConnection connection, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.Name), value => connection.Name = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.DisplayText), value => connection.DisplayText = value);

        if (!json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.Source), value => connection.Source = value) &&
            !json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.ClientName), value => connection.Source = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.ProviderName), value => connection.Source = value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.OwnerId), value => connection.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProviderConnection.Author), value => connection.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIProviderConnection.CreatedUtc), out var createdUtc))
        {
            connection.CreatedUtc = createdUtc;
        }

        MergeProperties(connection, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(AIProviderConnection connection, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIProviderConnection.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(connection.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        connection.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }
}
