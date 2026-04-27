using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIProfileTemplateCatalogHandler : CatalogEntryHandlerBase<AIProfileTemplate>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly INamedSourceCatalog<AIProfileTemplate> _templatesCatalog;

    internal readonly IStringLocalizer S;

    public AIProfileTemplateCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        INamedSourceCatalog<AIProfileTemplate> templatesCatalog,
        IStringLocalizer<AIProfileTemplateCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _templatesCatalog = templatesCatalog;
        S = stringLocalizer;
    }

    public override Task InitializingAsync(InitializingContext<AIProfileTemplate> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, true);

    public override Task UpdatingAsync(UpdatingContext<AIProfileTemplate> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, false);

    public override Task InitializedAsync(InitializedContext<AIProfileTemplate> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<AIProfileTemplate> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidatingContext<AIProfileTemplate> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Template name is required."], [nameof(AIProfileTemplate.Name)]));
        }
        else
        {
            var existing = await _templatesCatalog.FindByNameAsync(context.Model.Name, cancellationToken);

            if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
            {
                context.Result.Fail(new ValidationResult(S["A template with this name already exists. The name must be unique."], [nameof(AIProfileTemplate.Name)]));
            }
        }

        if (string.IsNullOrWhiteSpace(context.Model.DisplayText))
        {
            context.Result.Fail(new ValidationResult(S["Title is required."], [nameof(AIProfileTemplate.DisplayText)]));
        }
    }

    private void EnsureCreatedDefaults(AIProfileTemplate template)
    {
        if (template.CreatedUtc == default)
        {
            template.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        if (string.IsNullOrWhiteSpace(template.DisplayText))
        {
            template.DisplayText = template.Name;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        template.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        template.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(AIProfileTemplate template, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.Name), value => template.Name = value);
            json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.Source), value => template.Source = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.DisplayText), value => template.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.Description), value => template.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.Category), value => template.Category = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.OwnerId), value => template.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfileTemplate.Author), value => template.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIProfileTemplate.CreatedUtc), out var createdUtc))
        {
            template.CreatedUtc = createdUtc;
        }

        if (json.TryGetBooleanValue(nameof(AIProfileTemplate.IsListable), out var isListable))
        {
            template.IsListable = isListable;
        }

        MergeProperties(template, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(AIProfileTemplate template, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIProfileTemplate.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(template.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        template.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }
}
