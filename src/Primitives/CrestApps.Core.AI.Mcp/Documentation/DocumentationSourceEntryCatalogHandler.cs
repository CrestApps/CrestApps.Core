using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// The authoritative catalog handler for <see cref="DocumentationSourceEntry"/>. It populates entries
/// from JSON, sets create-time defaults, and validates that the strategy and its required fields are
/// present so store-backed sources can be created and edited safely through a UI or database.
/// </summary>
internal sealed class DocumentationSourceEntryCatalogHandler : CatalogEntryHandlerBase<DocumentationSourceEntry>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IDocumentationSourceCatalog _catalog;
    private readonly HashSet<string> _strategies;

    private readonly IStringLocalizer S;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationSourceEntryCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="catalog">The documentation source catalog used to enforce unique names.</param>
    /// <param name="factories">The registered strategy factories used to validate the strategy.</param>
    /// <param name="stringLocalizer">The string localizer.</param>
    public DocumentationSourceEntryCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IDocumentationSourceCatalog catalog,
        IEnumerable<IDocumentationSourceFactory> factories,
        IStringLocalizer<DocumentationSourceEntryCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _catalog = catalog;
        _strategies = new HashSet<string>(factories.Select(factory => factory.Strategy), StringComparer.OrdinalIgnoreCase);
        S = stringLocalizer;
    }

    /// <inheritdoc />
    public override Task InitializingAsync(InitializingContext<DocumentationSourceEntry> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, true);

    /// <inheritdoc />
    public override async Task UpdatingAsync(UpdatingContext<DocumentationSourceEntry> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data, false);

        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public override Task InitializedAsync(InitializedContext<DocumentationSourceEntry> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task CreatingAsync(CreatingContext<DocumentationSourceEntry> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task ValidatingAsync(ValidatingContext<DocumentationSourceEntry> context, CancellationToken cancellationToken = default)
    {
        var model = context.Model;

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Name is required."], [nameof(DocumentationSourceEntry.Name)]));
        }

        if (string.IsNullOrWhiteSpace(model.Source))
        {
            context.Result.Fail(new ValidationResult(S["Strategy is required."], [nameof(DocumentationSourceEntry.Strategy)]));
        }
        else if (!_strategies.Contains(model.Source))
        {
            context.Result.Fail(new ValidationResult(S["Unknown documentation search strategy '{0}'.", model.Source], [nameof(DocumentationSourceEntry.Strategy)]));
        }
        else
        {
            ValidateStrategyFields(context);
        }

        await ValidateUniqueNameAsync(context, cancellationToken);
    }

    private void ValidateStrategyFields(ValidatingContext<DocumentationSourceEntry> context)
    {
        var model = context.Model;

        if (string.Equals(model.Source, DocumentationSourceStrategies.Algolia, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(model.ApplicationId))
            {
                context.Result.Fail(new ValidationResult(S["Application Id is required for the Algolia strategy."], [nameof(DocumentationSourceEntry.ApplicationId)]));
            }

            if (string.IsNullOrWhiteSpace(model.ApiKey))
            {
                context.Result.Fail(new ValidationResult(S["API Key is required for the Algolia strategy."], [nameof(DocumentationSourceEntry.ApiKey)]));
            }

            if (string.IsNullOrWhiteSpace(model.IndexName))
            {
                context.Result.Fail(new ValidationResult(S["Index Name is required for the Algolia strategy."], [nameof(DocumentationSourceEntry.IndexName)]));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            context.Result.Fail(new ValidationResult(S["Base URL is required for the '{0}' strategy.", model.Source], [nameof(DocumentationSourceEntry.BaseUrl)]));
        }
    }

    private void EnsureCreatedDefaults(DocumentationSourceEntry entry)
    {
        if (entry.CreatedUtc == default)
        {
            entry.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return;
        }

        entry.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        entry.Author ??= user.Identity?.Name;
    }

    private async Task ValidateUniqueNameAsync(ValidatingContext<DocumentationSourceEntry> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            return;
        }

        var existing = await _catalog.FindByNameAsync(context.Model.Name, cancellationToken);

        if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
        {
            context.Result.Fail(new ValidationResult(S["A documentation source with this name already exists. The name must be unique."], [nameof(DocumentationSourceEntry.Name)]));
        }
    }

    private static Task PopulateAsync(DocumentationSourceEntry entry, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.Name), value => entry.Name = value);
        }

        if (!json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.Strategy), value => entry.Source = value))
        {
            json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.Source), value => entry.Source = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.DisplayText), value => entry.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.BaseUrl), value => entry.BaseUrl = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.SitemapUrl), value => entry.SitemapUrl = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.IndexUrl), value => entry.IndexUrl = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.ApplicationId), value => entry.ApplicationId = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.ApiKey), value => entry.ApiKey = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.IndexName), value => entry.IndexName = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.OwnerId), value => entry.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(DocumentationSourceEntry.Author), value => entry.Author = value);

        if (json.TryGetNullableInt32Value(nameof(DocumentationSourceEntry.MaxResults), out var maxResults))
        {
            entry.MaxResults = maxResults;
        }

        if (json.TryGetNullableInt32Value(nameof(DocumentationSourceEntry.MaxPages), out var maxPages))
        {
            entry.MaxPages = maxPages;
        }

        if (json.TryGetDateTimeValue(nameof(DocumentationSourceEntry.CreatedUtc), out var createdUtc))
        {
            entry.CreatedUtc = createdUtc;
        }

        return Task.CompletedTask;
    }
}
