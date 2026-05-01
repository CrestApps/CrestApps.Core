using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Persists and queries extracted-data snapshot records for chat sessions.
/// </summary>
public interface IAIChatSessionExtractedDataStore
{
    /// <summary>
    /// Saves the extracted-data snapshot record, creating or updating the existing
    /// record for the same chat session.
    /// </summary>
    /// <param name="record">The extracted-data snapshot record to save.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveAsync(
        AIChatSessionExtractedDataRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the extracted-data snapshot record for the specified chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a record was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves extracted-data snapshot records for the specified AI profile and
    /// optional date range.
    /// </summary>
    /// <param name="profileId">The AI profile identifier.</param>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(
        string profileId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default);
}
