using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Default framework recorder that persists extracted chat-session values through
/// the configured <see cref="IAIChatSessionExtractedDataStore"/>.
/// </summary>
public sealed class DefaultAIChatSessionExtractedDataRecorder : IAIChatSessionExtractedDataRecorder
{
    private readonly IAIChatSessionExtractedDataStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIChatSessionExtractedDataRecorder"/> class.
    /// </summary>
    /// <param name="store">The extracted-data store.</param>
    /// <param name="timeProvider">The time provider.</param>
    public DefaultAIChatSessionExtractedDataRecorder(
        IAIChatSessionExtractedDataStore store,
        TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records the current extracted-data snapshot for the specified chat session.
    /// </summary>
    /// <param name="profile">The AI profile associated with the session.</param>
    /// <param name="session">The chat session to record extracted data for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RecordExtractedDataAsync(
        AIProfile profile,
        AIChatSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        var values = session.ExtractedData
            .Where(pair => pair.Value.Values.Count > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Values.ToList(),
                StringComparer.OrdinalIgnoreCase);

        if (values.Count == 0)
        {
            await _store.DeleteAsync(session.SessionId, cancellationToken);

            return;
        }

        await _store.SaveAsync(
            new AIChatSessionExtractedDataRecord
            {
                ItemId = session.SessionId,
                SessionId = session.SessionId,
                ProfileId = profile.ItemId,
                SessionStartedUtc = session.CreatedUtc,
                SessionEndedUtc = session.ClosedAtUtc,
                UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
                Values = values,
            },
            cancellationToken);
    }
}
