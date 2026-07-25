using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// The authoritative catalog handler for <see cref="AIToolDefinition"/> entries. It maps incoming JSON
/// onto the model, applies create-time defaults, and validates the shared model concerns (the tool
/// source and the description used to disambiguate definitions). Source-specific settings validation is
/// intentionally left to the presentation layer.
/// </summary>
internal sealed class AIToolDefinitionCatalogHandler : CatalogEntryHandlerBase<AIToolDefinition>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _sourceNames;

    internal readonly IStringLocalizer S;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolDefinitionCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor used to stamp owner details.</param>
    /// <param name="timeProvider">The time provider used for create/modify timestamps.</param>
    /// <param name="sources">The registered tool sources used to validate the selected source.</param>
    /// <param name="stringLocalizer">The string localizer used for validation messages.</param>
    public AIToolDefinitionCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IEnumerable<AIToolSource> sources,
        IStringLocalizer<AIToolDefinitionCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _sourceNames = new HashSet<string>(
            sources.Where(source => !string.IsNullOrEmpty(source.Name)).Select(source => source.Name),
            StringComparer.OrdinalIgnoreCase);
        S = stringLocalizer;
    }

    /// <summary>
    /// Populates a new definition from the supplied JSON data.
    /// </summary>
    /// <param name="context">The initializing context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<AIToolDefinition> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, true);

    /// <summary>
    /// Populates an existing definition from the supplied JSON data and updates the modified timestamp.
    /// </summary>
    /// <param name="context">The updating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task UpdatingAsync(UpdatingContext<AIToolDefinition> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data, false);

        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Applies create-time defaults after initialization.
    /// </summary>
    /// <param name="context">The initialized context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<AIToolDefinition> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies create-time defaults before the definition is persisted.
    /// </summary>
    /// <param name="context">The creating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<AIToolDefinition> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates the shared model concerns for the definition.
    /// </summary>
    /// <param name="context">The validating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task ValidatingAsync(ValidatingContext<AIToolDefinition> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.DisplayText))
        {
            context.Result.Fail(new ValidationResult(
                S["Display text is required."], [nameof(AIToolDefinition.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Description))
        {
            context.Result.Fail(new ValidationResult(
                S["A description is required so the AI model can tell definitions apart."], [nameof(AIToolDefinition.Description)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(
                S["A tool source is required."], [nameof(AIToolDefinition.Source)]));
        }
        else if (!_sourceNames.Contains(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(
                S["The selected tool source is not registered."], [nameof(AIToolDefinition.Source)]));
        }

        return Task.CompletedTask;
    }

    private void EnsureCreatedDefaults(AIToolDefinition definition)
    {
        if (definition.CreatedUtc == default)
        {
            definition.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        definition.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        definition.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(AIToolDefinition definition, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(AIToolDefinition.Source), value => definition.Source = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(AIToolDefinition.DisplayText), value => definition.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolDefinition.Description), value => definition.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolDefinition.OwnerId), value => definition.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolDefinition.Author), value => definition.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIToolDefinition.CreatedUtc), out var createdUtc))
        {
            definition.CreatedUtc = createdUtc;
        }

        MergeProperties(definition, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(AIToolDefinition definition, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIToolDefinition.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(definition.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        definition.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }
}
