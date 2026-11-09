using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// The logs of an ingestion connector that could not list a folder or read a file. The run carries on without what
/// failed, so each has an event id to alert on. The ids sit in the ingestion range (4000s).
/// </summary>
internal static partial class FileSourceLog
{
    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning, Message = "Failed to list '{RootPath}' for source '{FileSourceId}'.")]
    public static partial void RemoteListFailed(ILogger logger, Exception exception, string rootPath, string fileSourceId);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "Failed to read '{ItemId}' for source '{FileSourceId}'.")]
    public static partial void RemoteReadFailed(ILogger logger, Exception exception, string itemId, string fileSourceId);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Warning, Message = "Failed to list files under '{RootPath}'.")]
    public static partial void FolderListFailed(ILogger logger, Exception exception, string rootPath);

    [LoggerMessage(EventId = 4304, Level = LogLevel.Warning, Message = "Failed to read '{Path}'.")]
    public static partial void FileReadFailed(ILogger logger, Exception exception, string path);
}
