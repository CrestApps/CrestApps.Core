using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Services;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
/// <summary>
/// Asks OpenAI's speech endpoint for raw PCM by the name the realtime pipeline uses, at the rate asked for.
/// </summary>
/// <remarks>
/// A cascaded realtime session asks its speaking leg for <c>Pcm{rate}</c>, the naming Azure Speech uses, at the rate
/// the session plays. OpenAI names the same audio <c>pcm</c> and produces it at 24 kHz only, and it refuses a format
/// name it does not know, so every reply of a cascade with an OpenAI speaking leg would fail. Raw PCM at another rate
/// (8 kHz or 16 kHz for a phone line, for example) is asked for at 24 kHz and converted, a chunk at a time as it
/// streams. Every other format name is passed on unchanged.
/// </remarks>
internal sealed class OpenAITextToSpeechFormatClient : DelegatingTextToSpeechClient
{
    /// <summary>
    /// The only rate OpenAI produces raw PCM at: 16-bit, little-endian, mono, without a header.
    /// </summary>
    internal const int PcmSampleRate = 24000;

    private const string PcmFormatPrefix = "Pcm";

    private const string OpenAIPcmFormat = "pcm";

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAITextToSpeechFormatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The OpenAI text-to-speech client.</param>
    public OpenAITextToSpeechFormatClient(ITextToSpeechClient innerClient)
        : base(innerClient)
    {
    }

    /// <summary>
    /// Synthesizes the text in the format OpenAI names for the one asked for, at the rate asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var (named, rate) = InOpenAIFormat(options);
        var response = await base.GetAudioAsync(text, named, cancellationToken);

        if (rate == PcmSampleRate)
        {
            return response;
        }

        for (var i = 0; i < response.Contents.Count; i++)
        {
            if (response.Contents[i] is DataContent audio && IsRawPcm(audio))
            {
                var resampler = new PcmResampler(PcmSampleRate, rate);
                response.Contents[i] = new DataContent((byte[])[.. resampler.Process(audio.Data.Span), .. resampler.Flush()], audio.MediaType);
            }
        }

        return response;
    }

    /// <summary>
    /// Synthesizes the text as a stream of audio updates, in the format OpenAI names for the one asked for, at the
    /// rate asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var (named, rate) = InOpenAIFormat(options);
        var updates = base.GetStreamingAudioAsync(text, named, cancellationToken);

        return rate == PcmSampleRate
            ? updates
            : ResampleAsync(updates, rate, cancellationToken);
    }

    /// <summary>
    /// Converts the raw PCM of a stream of updates from OpenAI's rate to the one asked for.
    /// </summary>
    /// <remarks>
    /// One resampler serves the whole stream, so a chunk boundary leaves no click. The last few samples wait for the
    /// input after them, and come in an update of their own once the stream ends.
    /// </remarks>
    /// <param name="updates">The updates OpenAI streams, with raw PCM at 24 kHz.</param>
    /// <param name="rate">The rate asked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async IAsyncEnumerable<TextToSpeechResponseUpdate> ResampleAsync(
        IAsyncEnumerable<TextToSpeechResponseUpdate> updates,
        int rate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resampler = new PcmResampler(PcmSampleRate, rate);
        string mediaType = null;

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            for (var i = 0; i < update.Contents.Count; i++)
            {
                if (update.Contents[i] is DataContent audio && IsRawPcm(audio))
                {
                    mediaType = audio.MediaType;
                    update.Contents[i] = new DataContent(resampler.Process(audio.Data.Span), audio.MediaType);
                }
            }

            yield return update;
        }

        if (mediaType is null)
        {
            yield break;
        }

        var tail = resampler.Flush();

        if (tail.Length > 0)
        {
            yield return new TextToSpeechResponseUpdate([new DataContent(tail, mediaType)])
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
            };
        }
    }

    /// <summary>
    /// The options with a <c>Pcm{rate}</c> format named as OpenAI names it, and the rate the audio is wanted at.
    /// </summary>
    /// <param name="options">The options asked for.</param>
    private static (TextToSpeechOptions Options, int Rate) InOpenAIFormat(TextToSpeechOptions options)
    {
        var format = options?.AudioFormat;

        if (format is null ||
            format.Length <= PcmFormatPrefix.Length ||
            !format.StartsWith(PcmFormatPrefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(format.AsSpan(PcmFormatPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var rate) ||
            rate <= 0)
        {
            return (options, PcmSampleRate);
        }

        var named = options.Clone();
        named.AudioFormat = OpenAIPcmFormat;

        return (named, rate);
    }

    /// <summary>
    /// Determines whether the audio is the raw PCM asked for, rather than a format that carries its own rate.
    /// </summary>
    /// <param name="audio">The audio OpenAI returned.</param>
    private static bool IsRawPcm(DataContent audio)
        => !audio.Data.IsEmpty &&
            (string.Equals(audio.MediaType, "audio/l16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(audio.MediaType, "audio/pcm", StringComparison.OrdinalIgnoreCase));
}
#pragma warning restore MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
