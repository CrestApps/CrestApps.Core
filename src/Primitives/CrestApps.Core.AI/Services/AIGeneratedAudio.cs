using System.Buffers.Binary;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Measures the audio a text-to-speech response carried: how many bytes it is and, for the formats whose length
/// can be read from the bytes alone, how long it plays.
/// </summary>
internal readonly record struct AIGeneratedAudio(long Bytes, long DurationMs, string MediaType)
{
    /// <summary>
    /// Measures the audio in the contents of a text-to-speech response or its updates.
    /// </summary>
    /// <param name="contents">The contents.</param>
    public static AIGeneratedAudio Measure(IEnumerable<AIContent> contents)
    {
        if (contents is null)
        {
            return default;
        }

        long bytes = 0;
        long durationMs = 0;
        string mediaType = null;
        var durationKnown = true;

        foreach (var content in contents.OfType<DataContent>())
        {
            bytes += content.Data.Length;
            mediaType ??= content.MediaType;

            if (TryGetWavDurationMs(content.Data.Span, out var chunkDurationMs))
            {
                durationMs += chunkDurationMs;
            }
            else
            {
                durationKnown = false;
            }
        }

        return new AIGeneratedAudio(bytes, durationKnown ? durationMs : 0, mediaType);
    }

    /// <summary>
    /// Reads the length of a PCM WAV file from its header: the size of its data chunk divided by its byte rate.
    /// </summary>
    /// <param name="data">The bytes of the file.</param>
    /// <param name="durationMs">The length in milliseconds.</param>
    internal static bool TryGetWavDurationMs(ReadOnlySpan<byte> data, out long durationMs)
    {
        durationMs = 0;

        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var byteRate = 0;
        var position = 12;

        while (position + 8 <= data.Length)
        {
            var chunkId = data.Slice(position, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 4, 4));
            var body = position + 8;

            if (chunkId.SequenceEqual("fmt "u8) && chunkSize >= 12 && body + 12 <= data.Length)
            {
                byteRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(body + 8, 4));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                if (byteRate <= 0)
                {
                    return false;
                }

                // A streamed file can declare an unknown size, in which case what was received is what plays.
                var length = chunkSize == uint.MaxValue || body + chunkSize > data.Length
                    ? (long)data.Length - body
                    : chunkSize;

                durationMs = (long)(length * 1000L / byteRate);

                return true;
            }

            if (chunkSize > int.MaxValue - 1)
            {
                return false;
            }

            position = body + (int)chunkSize + (int)(chunkSize & 1);
        }

        return false;
    }
}
