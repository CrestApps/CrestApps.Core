using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// The log messages of document processing whose failures are logged and then swallowed, each with its own event
/// id in the 4000 range.
/// </summary>
internal static partial class DocumentProcessingLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Document processing: failed to read file '{FileName}' with extension '{Extension}'.")]
    public static partial void ReadFailed(ILogger logger, Exception exception, string fileName, string extension);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Failed to generate embeddings for '{FileName}'. Chunks will be stored without embeddings.")]
    public static partial void EmbeddingFailed(ILogger logger, Exception exception, string fileName);
}
