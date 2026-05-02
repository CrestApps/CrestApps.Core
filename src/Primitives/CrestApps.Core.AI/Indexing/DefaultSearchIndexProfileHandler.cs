using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the default Search Index Profile Handler.
/// </summary>
public sealed class DefaultSearchIndexProfileHandler : IndexProfileHandlerBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ISearchIndexProfileStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSearchIndexProfileHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    public DefaultSearchIndexProfileHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ISearchIndexProfileStore store)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _store = store;
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task UpdatingAsync(UpdatingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Initializeds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override ValueTask ValidateAsync(
        SearchIndexProfile indexProfile,
        ValidationResultDetails result,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexProfile.Name))
        {
            result.Fail(new ValidationResult("Name is required.", [nameof(SearchIndexProfile.Name)]));
        }

        if (string.IsNullOrWhiteSpace(indexProfile.IndexName))
        {
            result.Fail(new ValidationResult("Index name is required.", [nameof(SearchIndexProfile.IndexName)]));
        }

        if (string.IsNullOrWhiteSpace(indexProfile.ProviderName))
        {
            result.Fail(new ValidationResult("Provider is required.", [nameof(SearchIndexProfile.ProviderName)]));
        }

        if (string.IsNullOrWhiteSpace(indexProfile.Type))
        {
            result.Fail(new ValidationResult("Type is required.", [nameof(SearchIndexProfile.Type)]));
        }

        return ValidateUniqueNameAsync(indexProfile, result, cancellationToken);
    }

    private void EnsureCreatedDefaults(SearchIndexProfile indexProfile)
    {
        if (indexProfile.CreatedUtc == default)
        {
            indexProfile.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        indexProfile.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        indexProfile.Author ??= user.Identity?.Name;
    }

    private async ValueTask ValidateUniqueNameAsync(
        SearchIndexProfile indexProfile,
        ValidationResultDetails result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexProfile.Name))
        {
            return;
        }

        var existing = await _store.FindByNameAsync(indexProfile.Name, cancellationToken);

        if (existing is not null && !string.Equals(existing.ItemId, indexProfile.ItemId, StringComparison.Ordinal))
        {
            result.Fail(new ValidationResult("A search index profile with the same name already exists.", [nameof(SearchIndexProfile.Name)]));
        }
    }

    private static Task PopulateAsync(SearchIndexProfile indexProfile, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.Name), value => indexProfile.Name = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.DisplayText), value => indexProfile.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.IndexName), value => indexProfile.IndexName = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.ProviderName), value => indexProfile.ProviderName = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.IndexFullName), value => indexProfile.IndexFullName = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.Type), value => indexProfile.Type = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.EmbeddingDeploymentName), value => indexProfile.EmbeddingDeploymentName = value);

        if (string.IsNullOrWhiteSpace(indexProfile.EmbeddingDeploymentName))
        {
            json.TryUpdateTrimmedStringValue("EmbeddingDeploymentId", value => indexProfile.EmbeddingDeploymentName = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.OwnerId), value => indexProfile.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(SearchIndexProfile.Author), value => indexProfile.Author = value);

        if (json.TryGetDateTimeValue(nameof(SearchIndexProfile.CreatedUtc), out var createdUtc))
        {
            indexProfile.CreatedUtc = createdUtc;
        }

        MergeProperties(indexProfile, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(SearchIndexProfile indexProfile, JsonObject json)
    {
        if (!json.TryGetPropertyValue(nameof(SearchIndexProfile.Properties), out var propertiesNode))
        {
            return;
        }

        indexProfile.Properties.Clear();

        if (propertiesNode is not JsonObject properties)
        {
            return;
        }

        foreach (var (key, value) in properties)
        {
            indexProfile.Properties[key] = value.GetRawValue();
        }
    }
}
