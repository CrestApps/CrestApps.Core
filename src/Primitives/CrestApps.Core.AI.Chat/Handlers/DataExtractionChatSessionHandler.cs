using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Handlers;

/// <summary>
/// Runs shared data extraction after each chat completion and closes the session
/// when the extraction result indicates the conversation naturally ended.
/// </summary>
public sealed class DataExtractionChatSessionHandler : AIChatSessionHandlerBase
{
    private readonly DataExtractionService _dataExtractionService;
    private readonly IEnumerable<IAIChatSessionExtractedDataRecorder> _extractedDataRecorders;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataExtractionChatSessionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataExtractionChatSessionHandler"/> class.
    /// </summary>
    /// <param name="dataExtractionService">The data extraction service.</param>
    /// <param name="extractedDataRecorders">The extracted data recorders.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DataExtractionChatSessionHandler(
        DataExtractionService dataExtractionService,
        IEnumerable<IAIChatSessionExtractedDataRecorder> extractedDataRecorders,
        TimeProvider timeProvider,
        ILogger<DataExtractionChatSessionHandler> logger)
    {
        _dataExtractionService = dataExtractionService;
        _extractedDataRecorders = extractedDataRecorders;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Messages completed.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task MessageCompletedAsync(ChatMessageCompletedContext context, CancellationToken cancellationToken = default)
    {
        var changeSet = await _dataExtractionService.ProcessAsync(context.Profile, context.ChatSession, context.Prompts, cancellationToken);

        if (changeSet is null)
        {
            return;
        }

        context.Items[AIChatSessionHandlerContextKeys.DataExtractionChangeSet] = changeSet;

        if (changeSet.SessionEnded && context.ChatSession.Status != ChatSessionStatus.Closed)
        {
            context.ChatSession.Status = ChatSessionStatus.Closed;
            context.ChatSession.ClosedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Session '{SessionId}' closed due to natural conversation ending.",
                    context.ChatSession.SessionId);
            }
        }

        if (changeSet.NewFields.Count > 0 || changeSet.SessionEnded)
        {
            foreach (var recorder in _extractedDataRecorders)
            {
                await recorder.RecordExtractedDataAsync(context.Profile, context.ChatSession, cancellationToken);
            }
        }
    }
}
