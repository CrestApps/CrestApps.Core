using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI;

/// <summary>
/// The log messages of the AI pipeline whose failures are logged and then swallowed, each with its own event
/// id in the 2000 and 5000 ranges, so they can be filtered and alerted on without matching message text.
/// </summary>
internal static partial class AILog
{
    /// <summary>
    /// The event id of a handler failure that was logged and skipped, so the loop could go on to the next handler.
    /// </summary>
    public static readonly EventId HandlerFailedEventId = new(2003, "HandlerFailed");

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "An error occurred while chatting with the {Name} service.")]
    public static partial void CompletionFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Planning phase failed. Falling back to full tool injection.")]
    public static partial void PlanningFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "The guardrails of AI profile '{ProfileId}' could not be checked.")]
    public static partial void GuardrailCheckFailed(ILogger logger, Exception exception, string profileId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "Failed to read full documents for data source '{DataSourceId}'. Falling back to chunk retrieval.")]
    public static partial void HierarchicalRetrievalFailed(ILogger logger, Exception exception, string dataSourceId);

    /// <summary>
    /// Counts the failure of one handler in a loop that logs it and skips it.
    /// </summary>
    /// <param name="handler">The handler that failed.</param>
    public static void CountHandlerFailure(object handler)
    {
        AITelemetry.HandlerErrors.Add(1, new KeyValuePair<string, object>(AITelemetry.HandlerTag, handler.GetType().Name));
    }
}
