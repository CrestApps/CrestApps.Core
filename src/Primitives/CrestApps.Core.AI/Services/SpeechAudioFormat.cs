using System.Globalization;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Reads the audio format a text-to-speech client is asked for in <c>TextToSpeechOptions.AudioFormat</c>.
/// </summary>
/// <remarks>
/// A realtime session names the raw PCM it plays as <c>Pcm</c> followed by the sample rate (<c>Pcm24000</c>). Anything
/// else, or nothing, means a compressed format the client chooses itself.
/// </remarks>
public static class SpeechAudioFormat
{
    /// <summary>
    /// The media type of raw 16-bit little-endian mono PCM a speech client returns.
    /// </summary>
    public const string PcmMediaType = "audio/pcm";

    /// <summary>
    /// Describes audio ready to upload to a recognizer.
    /// </summary>
    /// <param name="Content">The audio, with a container a recognizer can read.</param>
    /// <param name="ContentType">The media type of <paramref name="Content"/>.</param>
    /// <param name="FileName">A file name with the extension the recognizer expects.</param>
    public readonly record struct UploadableAudio(byte[] Content, string ContentType, string FileName);

    /// <summary>
    /// Prepares audio for a recognizer that takes files. Audio that already has a container (WAV, MP3, Ogg, FLAC,
    /// WebM) is passed through; anything else is taken for raw 16-bit mono PCM and wrapped in a WAV header, which is
    /// what a realtime session captures.
    /// </summary>
    /// <param name="audio">The audio.</param>
    /// <param name="pcmSampleRate">The sample rate to assume for raw PCM, or <see langword="null"/> for 16 kHz.</param>
    public static UploadableAudio ToUploadable(byte[] audio, int? pcmSampleRate)
    {
        ArgumentNullException.ThrowIfNull(audio);

        static bool StartsWith(byte[] data, params byte[] prefix)
            => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

        if (StartsWith(audio, (byte)'R', (byte)'I', (byte)'F', (byte)'F'))
        {
            return new UploadableAudio(audio, "audio/wav", "audio.wav");
        }

        if (StartsWith(audio, (byte)'I', (byte)'D', (byte)'3') || StartsWith(audio, 0xFF, 0xFB) || StartsWith(audio, 0xFF, 0xF3))
        {
            return new UploadableAudio(audio, "audio/mpeg", "audio.mp3");
        }

        if (StartsWith(audio, (byte)'O', (byte)'g', (byte)'g', (byte)'S'))
        {
            return new UploadableAudio(audio, "audio/ogg", "audio.ogg");
        }

        if (StartsWith(audio, (byte)'f', (byte)'L', (byte)'a', (byte)'C'))
        {
            return new UploadableAudio(audio, "audio/flac", "audio.flac");
        }

        if (StartsWith(audio, 0x1A, 0x45, 0xDF, 0xA3))
        {
            return new UploadableAudio(audio, "audio/webm", "audio.webm");
        }

        var rate = pcmSampleRate is > 0 ? pcmSampleRate.Value : 16000;
        var wav = new byte[44 + audio.Length];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + audio.Length);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BitConverter.TryWriteBytes(span[16..], 16);
        BitConverter.TryWriteBytes(span[20..], (short)1);
        BitConverter.TryWriteBytes(span[22..], (short)1);
        BitConverter.TryWriteBytes(span[24..], rate);
        BitConverter.TryWriteBytes(span[28..], rate * 2);
        BitConverter.TryWriteBytes(span[32..], (short)2);
        BitConverter.TryWriteBytes(span[34..], (short)16);
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], audio.Length);
        audio.CopyTo(span[44..]);

        return new UploadableAudio(wav, "audio/wav", "audio.wav");
    }

    /// <summary>
    /// Reads the sample rate of a requested raw PCM format.
    /// </summary>
    /// <param name="audioFormat">The requested format, such as <c>Pcm24000</c>.</param>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    /// <returns><see langword="true"/> when the format is raw PCM.</returns>
    public static bool TryGetPcmSampleRate(string audioFormat, out int sampleRate)
    {
        sampleRate = 0;

        return audioFormat is { Length: > 3 }
            && audioFormat.StartsWith("Pcm", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(audioFormat.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out sampleRate)
            && sampleRate > 0;
    }
}
