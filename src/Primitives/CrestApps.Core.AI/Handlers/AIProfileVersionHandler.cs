using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Numbers each save of an AI profile and keeps the profile as it was saved, so its history can be compared and an
/// earlier version restored. The number is kept on the profile in <see cref="AIProfileVersionMetadata"/>, and new
/// chat sessions record it.
/// </summary>
public sealed class AIProfileVersionHandler : CatalogEntryHandlerBase<AIProfile>
{
    private readonly IAIProfileVersionStore _versionStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    // The note a save carries, held from numbering to keeping so it never reaches the stored profile.
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileVersionHandler"/> class.
    /// </summary>
    /// <param name="versionStore">The store the versions are kept in.</param>
    /// <param name="httpContextAccessor">The HTTP context accessor, which names who saved the profile.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AIProfileVersionHandler(
        IAIProfileVersionStore versionStore,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _versionStore = versionStore;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Numbers a new profile's first version.
    /// </summary>
    /// <param name="context">The creating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the profile is numbered.</returns>
    public override Task CreatingAsync(CreatingContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return NumberAsync(context.Model, cancellationToken);
    }

    /// <summary>
    /// Numbers the next version of a profile being updated.
    /// </summary>
    /// <param name="context">The updating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the profile is numbered.</returns>
    public override Task UpdatingAsync(UpdatingContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return NumberAsync(context.Model, cancellationToken);
    }

    /// <summary>
    /// Keeps the created profile as its first version.
    /// </summary>
    /// <param name="context">The created context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the version is kept.</returns>
    public override Task CreatedAsync(CreatedContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return KeepAsync(context.Model, cancellationToken);
    }

    /// <summary>
    /// Keeps the updated profile as its next version.
    /// </summary>
    /// <param name="context">The updated context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the version is kept.</returns>
    public override Task UpdatedAsync(UpdatedContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return KeepAsync(context.Model, cancellationToken);
    }

    private async Task NumberAsync(AIProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(profile?.ItemId))
        {
            return;
        }

        var metadata = profile.GetOrCreate<AIProfileVersionMetadata>();
        var latest = await _versionStore.GetLatestVersionAsync(profile.ItemId, cancellationToken);
        metadata.Version = Math.Max(latest, metadata.Version) + 1;
        _notes[profile.ItemId] = metadata.PendingNote;
        metadata.PendingNote = null;
        profile.Put(metadata);
    }

    private async Task KeepAsync(AIProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(profile?.ItemId) ||
            !profile.TryGet<AIProfileVersionMetadata>(out var metadata) ||
            metadata.Version <= 0)
        {
            return;
        }

        _notes.Remove(profile.ItemId, out var note);

        await _versionStore.SaveAsync(new AIProfileVersion
        {
            ItemId = UniqueId.GenerateId(),
            ProfileId = profile.ItemId,
            Version = metadata.Version,
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Author = _httpContextAccessor.HttpContext?.User?.Identity?.Name,
            Note = note,
            Snapshot = profile.Clone(),
        }, cancellationToken);
    }
}
