using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
/// <summary>
/// A realtime session that records the usage reported on each completed response and each completed
/// transcription as its messages are read.
/// </summary>
internal sealed class AIUsageTrackingRealtimeSession : IRealtimeClientSession
{
    private readonly IRealtimeClientSession _innerSession;
    private readonly AIUsageRecorder _recorder;
    private readonly IReadOnlyDictionary<string, object> _usageProperties;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingRealtimeSession"/> class.
    /// </summary>
    /// <param name="innerSession">The provider's session.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    /// <param name="usageProperties">The labels and conversation captured when the session started.</param>
    public AIUsageTrackingRealtimeSession(
        IRealtimeClientSession innerSession,
        AIUsageRecorder recorder,
        IReadOnlyDictionary<string, object> usageProperties)
    {
        _innerSession = innerSession;
        _recorder = recorder;
        _usageProperties = usageProperties;
    }

    /// <summary>
    /// Gets the current session options.
    /// </summary>
    public RealtimeSessionOptions Options => _innerSession.Options;

    /// <summary>
    /// Sends a message to the provider.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        return _innerSession.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Reads the provider's messages, recording the usage reported on completed responses and transcriptions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _innerSession.GetStreamingResponseAsync(cancellationToken))
        {
            await RecordAsync(message);

            yield return message;
        }
    }

    /// <summary>
    /// Gets a service from the provider's session.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The optional service key.</param>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _innerSession.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes the provider's session.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return _innerSession.DisposeAsync();
    }

    private Task RecordAsync(RealtimeServerMessage message)
    {
        // Usage is recorded without the read's cancellation token: the session is often being torn down when the
        // last response finishes, and that response was still billed.
        if (message is ResponseCreatedRealtimeServerMessage { Usage: { } responseUsage } response &&
            response.Type == RealtimeServerMessageType.ResponseDone)
        {
            return _recorder.RecordAsync(
                AIUsageOperationTypes.Realtime,
                _usageProperties,
                Options?.Model,
                response.ResponseId,
                responseUsage,
                0,
                true,
                null,
                CancellationToken.None);
        }

        if (message is InputAudioTranscriptionRealtimeServerMessage { Usage: { } transcriptionUsage } transcription &&
            transcription.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            return _recorder.RecordAsync(
                AIUsageOperationTypes.RealtimeTranscription,
                _usageProperties,
                Options?.TranscriptionOptions?.ModelId,
                transcription.ItemId,
                transcriptionUsage,
                0,
                true,
                null,
                CancellationToken.None);
        }

        return Task.CompletedTask;
    }
}
#pragma warning restore MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
