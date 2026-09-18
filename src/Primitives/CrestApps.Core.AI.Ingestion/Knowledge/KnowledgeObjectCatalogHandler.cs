using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;

namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// Authoritative catalog handler for <see cref="KnowledgeObject"/>: applies create-time defaults, keeps the
/// modified timestamp honest, and refuses a record that could never be found again.
/// </summary>
internal sealed class KnowledgeObjectCatalogHandler : CatalogEntryHandlerBase<KnowledgeObject>
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeObjectCatalogHandler"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public KnowledgeObjectCatalogHandler(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public override Task InitializingAsync(InitializingContext<KnowledgeObject> context, CancellationToken cancellationToken = default)
    {
        return PopulateAsync(context.Model, context.Data);
    }

    /// <inheritdoc />
    public override async Task UpdatingAsync(UpdatingContext<KnowledgeObject> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data);

        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public override Task InitializedAsync(InitializedContext<KnowledgeObject> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task CreatingAsync(CreatingContext<KnowledgeObject> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ValidatingAsync(ValidatingContext<KnowledgeObject> context, CancellationToken cancellationToken = default)
    {
        var model = context.Model;

        if (string.IsNullOrWhiteSpace(model.CanonicalId))
        {
            context.Result.Fail(new ValidationResult("A canonical identifier is required.", [nameof(KnowledgeObject.CanonicalId)]));
        }

        if (string.IsNullOrWhiteSpace(model.ObjectType))
        {
            context.Result.Fail(new ValidationResult("An object type is required.", [nameof(KnowledgeObject.ObjectType)]));
        }

        if (string.IsNullOrWhiteSpace(model.RootId))
        {
            context.Result.Fail(new ValidationResult("A root identifier is required.", [nameof(KnowledgeObject.RootId)]));
        }

        if (string.IsNullOrWhiteSpace(model.Source))
        {
            context.Result.Fail(new ValidationResult("An owning data source is required.", [nameof(KnowledgeObject.Source)]));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps every settable property from the supplied data.
    /// </summary>
    /// <param name="model">The object being populated.</param>
    /// <param name="data">The supplied data.</param>
    private static Task PopulateAsync(KnowledgeObject model, JsonNode data)
    {
        if (data is not JsonObject node)
        {
            return Task.CompletedTask;
        }

        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.CanonicalId), value => model.CanonicalId = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.ObjectType), value => model.ObjectType = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.RootId), value => model.RootId = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.ParentId), value => model.ParentId = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.IndexerId), value => model.IndexerId = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.SourceItemId), value => model.SourceItemId = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Title), value => model.Title = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Content), value => model.Content = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Language), value => model.Language = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Folio), value => model.Folio = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.ContentHash), value => model.ContentHash = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.MediaType), value => model.MediaType = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.StoragePath), value => model.StoragePath = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Status), value => model.Status = value);
        node.TryUpdateTrimmedStringValue(nameof(KnowledgeObject.Source), value => model.Source = value);

        if (node.TryGetNullableInt32Value(nameof(KnowledgeObject.PageStart), out var pageStart))
        {
            model.PageStart = pageStart;
        }

        if (node.TryGetNullableInt32Value(nameof(KnowledgeObject.PageEnd), out var pageEnd))
        {
            model.PageEnd = pageEnd;
        }

        if (node.TryGetNullableInt32Value(nameof(KnowledgeObject.Ordinal), out var ordinal) && ordinal.HasValue)
        {
            model.Ordinal = ordinal.Value;
        }

        return Task.CompletedTask;
    }

    private void EnsureCreatedDefaults(KnowledgeObject model)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (model.CreatedUtc == default)
        {
            model.CreatedUtc = now;
        }

        model.ModifiedUtc ??= now;

        if (string.IsNullOrWhiteSpace(model.Status))
        {
            model.Status = KnowledgeObjectStatus.Ready;
        }
    }
}
