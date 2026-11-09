using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// The logs of ingestion work that failed without stopping the run, each with an event id to alert on. The ids sit
/// in the ingestion range (4000s).
/// </summary>
internal static partial class IngestionLog
{
    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Figure transcription could not be attempted for '{CanonicalId}'. It stays pending.")]
    public static partial void FigureTranscriptionDeferred(ILogger logger, Exception exception, string canonicalId);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Figure transcription failed for '{CanonicalId}'. The figure keeps its caption.")]
    public static partial void FigureTranscriptionFailed(ILogger logger, Exception exception, string canonicalId);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning, Message = "Failed to look up a cached description for '{CanonicalId}'.")]
    public static partial void FigureCacheLookupFailed(ILogger logger, Exception exception, string canonicalId);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Warning, Message = "Failed to read the stored figure '{StoragePath}'.")]
    public static partial void FigureReadFailed(ILogger logger, Exception exception, string storagePath);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Warning, Message = "Failed to read the vision deployment configured for source '{FileSourceId}'.")]
    public static partial void FigureDeploymentReadFailed(ILogger logger, Exception exception, string fileSourceId);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Debug, Message = "An item fetched by a run that stopped part-way failed or was cancelled. There was nothing left to release.")]
    public static partial void DiscardedFetchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4207, Level = LogLevel.Information, Message = "Source '{SourceId}' finished a run in {ElapsedMilliseconds} ms with status {Status}: {Indexed} indexed, {Skipped} skipped, {Deleted} deleted, {Failed} failed.")]
    public static partial void RunCompleted(ILogger logger, string sourceId, long elapsedMilliseconds, FileSourceRunStatus status, int indexed, int skipped, int deleted, int failed);
}
