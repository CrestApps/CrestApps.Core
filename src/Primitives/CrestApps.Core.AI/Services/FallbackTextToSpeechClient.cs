using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Synthesizes with a primary text-to-speech client and, when it fails before producing any audio, with a fallback
/// client instead. A failure after audio has started is rethrown: the two voices cannot be spliced mid-sentence.
/// </summary>
public sealed class FallbackTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly ITextToSpeechClient _fallback;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FallbackTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="primary">The client tried first.</param>
    /// <param name="fallback">The client used when the primary fails.</param>
    /// <param name="logger">The logger.</param>
    public FallbackTextToSpeechClient(ITextToSpeechClient primary, ITextToSpeechClient fallback, ILogger logger = null)
        : base(primary)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        _fallback = fallback;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetAudioAsync(text, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(ex);

            return await _fallback.GetAudioAsync(text, ForFallback(options), cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var started = false;
        var failed = false;
        var enumerator = base.GetStreamingAudioAsync(text, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (!started && ex is not OperationCanceledException)
                {
                    LogFailure(ex);
                    failed = true;

                    break;
                }

                if (!hasNext)
                {
                    yield break;
                }

                started = true;

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (!failed)
        {
            yield break;
        }

        await foreach (var update in _fallback.GetStreamingAudioAsync(text, ForFallback(options), cancellationToken))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fallback.Dispose();
        }

        base.Dispose(disposing);
    }

    // A voice id and model id belong to one provider; sending the primary's to the fallback would ask it for a voice
    // it has never heard of. The fallback speaks with its own deployment's defaults.
    private static TextToSpeechOptions ForFallback(TextToSpeechOptions options)
    {
        var clone = options?.Clone() ?? new TextToSpeechOptions();
        clone.VoiceId = null;
        clone.ModelId = null;

        return clone;
    }

    private void LogFailure(Exception exception)
    {
        _logger?.LogWarning(exception, "Text-to-speech failed; retrying with the fallback deployment.");
    }
}
#pragma warning restore MEAI001
