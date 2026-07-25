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
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// The authoritative catalog handler for <see cref="AIToolInstance"/> entries. It maps incoming JSON
/// onto the model, applies create-time defaults, and validates the shared model concerns (definition
/// source and the description used to disambiguate instances). Definition-specific settings validation
/// is intentionally left to the presentation layer.
/// </summary>
internal sealed class AIToolInstanceCatalogHandler : CatalogEntryHandlerBase<AIToolInstance>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly AIToolInstanceDefinitionOptions _definitionOptions;

    internal readonly IStringLocalizer S;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor used to stamp owner details.</param>
    /// <param name="timeProvider">The time provider used for create/modify timestamps.</param>
    /// <param name="definitionOptions">The registered tool instance definition metadata.</param>
    /// <param name="stringLocalizer">The string localizer used for validation messages.</param>
    public AIToolInstanceCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IOptions<AIToolInstanceDefinitionOptions> definitionOptions,
        IStringLocalizer<AIToolInstanceCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _definitionOptions = definitionOptions.Value;
        S = stringLocalizer;
    }

    /// <summary>
    /// Populates a new instance from the supplied JSON data.
    /// </summary>
    /// <param name="context">The initializing context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<AIToolInstance> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, true);

    /// <summary>
    /// Populates an existing instance from the supplied JSON data and updates the modified timestamp.
    /// </summary>
    /// <param name="context">The updating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task UpdatingAsync(UpdatingContext<AIToolInstance> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data, false);

        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Applies create-time defaults after initialization.
    /// </summary>
    /// <param name="context">The initialized context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<AIToolInstance> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies create-time defaults before the instance is persisted.
    /// </summary>
    /// <param name="context">The creating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<AIToolInstance> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates the shared model concerns for the instance.
    /// </summary>
    /// <param name="context">The validating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task ValidatingAsync(ValidatingContext<AIToolInstance> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.DisplayText))
        {
            context.Result.Fail(new ValidationResult(
                S["Display text is required."], [nameof(AIToolInstance.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Description))
        {
            context.Result.Fail(new ValidationResult(
                S["A description is required so the AI model can tell instances apart."], [nameof(AIToolInstance.Description)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(
                S["A tool definition is required."], [nameof(AIToolInstance.Source)]));
        }
        else if (!_definitionOptions.Definitions.ContainsKey(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult(
                S["The selected tool definition is not registered."], [nameof(AIToolInstance.Source)]));
        }

        return Task.CompletedTask;
    }

    private void EnsureCreatedDefaults(AIToolInstance instance)
    {
        if (instance.CreatedUtc == default)
        {
            instance.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        instance.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        instance.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(AIToolInstance instance, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(AIToolInstance.Source), value => instance.Source = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(AIToolInstance.DisplayText), value => instance.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolInstance.Description), value => instance.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolInstance.OwnerId), value => instance.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIToolInstance.Author), value => instance.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIToolInstance.CreatedUtc), out var createdUtc))
        {
            instance.CreatedUtc = createdUtc;
        }

        MergeProperties(instance, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(AIToolInstance instance, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIToolInstance.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(instance.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        instance.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }
}
