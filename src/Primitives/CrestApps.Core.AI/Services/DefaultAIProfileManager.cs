using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides the default framework implementation of <see cref="IAIProfileManager"/>.
/// </summary>
public sealed class DefaultAIProfileManager : NamedCatalogManager<AIProfile>, IAIProfileManager
{
    private readonly IAIProfileStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIProfileManager"/> class.
    /// </summary>
    /// <param name="store">The profile catalog.</param>
    /// <param name="handlers">The catalog entry handlers.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIProfileManager(
        IAIProfileStore store,
        IEnumerable<ICatalogEntryHandler<AIProfile>> handlers,
        TimeProvider timeProvider,
        ILogger<DefaultAIProfileManager> logger)
        : base(store, handlers, logger)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets AI profiles by type.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIProfile>> GetAsync(AIProfileType type, CancellationToken cancellationToken = default)
    {
        var profiles = await _store.GetByTypeAsync(type, cancellationToken);

        foreach (var profile in profiles)
        {
            await LoadAsync(profile, cancellationToken);
        }

        return profiles;
    }

    /// <summary>
    /// Creates a new AI profile instance.
    /// </summary>
    /// <param name="data">The optional initialization data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<AIProfile> NewAsync(JsonNode data = null, CancellationToken cancellationToken = default)
    {
        var profile = await base.NewAsync(data, cancellationToken);

        EnsureDefaults(profile);

        return profile;
    }

    /// <summary>
    /// Creates the specified AI profile.
    /// </summary>
    /// <param name="model">The profile to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask CreateAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        EnsureDefaults(model);

        await base.CreateAsync(model, cancellationToken);
    }

    /// <summary>
    /// Validates the specified AI profile.
    /// </summary>
    /// <param name="model">The profile to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<ValidationResultDetails> ValidateAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = await base.ValidateAsync(model, cancellationToken);

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult("Name is required.", [nameof(model.Name)]));
        }

        return result;
    }

    ValueTask<AIProfile> ICatalogManager<AIProfile>.NewAsync(JsonNode data, CancellationToken cancellationToken)
        => NewAsync(data, cancellationToken);

    ValueTask ICatalogManager<AIProfile>.CreateAsync(AIProfile model, CancellationToken cancellationToken)
        => CreateAsync(model, cancellationToken);

    ValueTask<ValidationResultDetails> ICatalogManager<AIProfile>.ValidateAsync(AIProfile model, CancellationToken cancellationToken)
        => ValidateAsync(model, cancellationToken);

    private void EnsureDefaults(AIProfile profile)
    {
        if (string.IsNullOrEmpty(profile.ItemId))
        {
            profile.ItemId = UniqueId.GenerateId();
        }

        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = _timeProvider.GetUtcNow().DateTime;
        }
    }
}
