using System.Globalization;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Services;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
/// <summary>
/// Asks OpenAI's speech endpoint for raw PCM by the name the realtime pipeline uses.
/// </summary>
/// <remarks>
/// A cascaded realtime session asks its speaking leg for <c>Pcm{rate}</c>, the naming Azure Speech uses, at the rate
/// the session plays. OpenAI names the same audio <c>pcm</c> and produces it at 24 kHz only, and it refuses a format
/// name it does not know, so every reply of a cascade with an OpenAI speaking leg would fail. Every other format name
/// is passed on unchanged.
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
    /// Synthesizes the text in the format OpenAI names for the one asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(text, InOpenAIFormat(options), cancellationToken);
    }

    /// <summary>
    /// Synthesizes the text as a stream of audio updates, in the format OpenAI names for the one asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(text, InOpenAIFormat(options), cancellationToken);
    }

    /// <summary>
    /// The options with a <c>Pcm{rate}</c> format named as OpenAI names it.
    /// </summary>
    /// <param name="options">The options asked for.</param>
    /// <exception cref="NotSupportedException">Raw PCM is asked for at a rate other than 24 kHz.</exception>
    private static TextToSpeechOptions InOpenAIFormat(TextToSpeechOptions options)
    {
        var format = options?.AudioFormat;

        if (format is null ||
            format.Length <= PcmFormatPrefix.Length ||
            !format.StartsWith(PcmFormatPrefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(format.AsSpan(PcmFormatPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var rate))
        {
            return options;
        }

        // Audio at another rate would be played at the wrong speed, so it is refused rather than mislabeled.
        if (rate != PcmSampleRate)
        {
            throw new NotSupportedException(
                $"OpenAI produces raw PCM only at {PcmSampleRate} Hz, but '{format}' asks for {rate} Hz. " +
                $"Ask for 'Pcm{PcmSampleRate}', or for a format that carries its own rate, such as 'wav'.");
        }

        var named = options.Clone();
        named.AudioFormat = OpenAIPcmFormat;

        return named;
    }
}
#pragma warning restore MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
