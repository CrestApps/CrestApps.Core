#pragma warning disable MEAI001
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Audio usage is reported as hours, request counts, characters, bytes, and tokens per model, voice, or language.
/// </summary>
public sealed class AIAudioUsageTests
{
    [Fact]
    public void Build_SumsAudioUnitsPerModelAndOperation()
    {
        var records = new[]
        {
            Stt("whisper", audioDurationMs: 3_600_000, audioStartMs: 600_000, inputBytes: 1_000, segments: 4),
            Stt("whisper", audioDurationMs: 1_800_000, audioStartMs: 0, inputBytes: 500, segments: 2),
            Tts("tts-1", characters: 120, durationMs: 60_000, outputBytes: 2_000, voice: "alloy"),
            new AICompletionUsageRecord { OperationType = AIUsageOperationTypes.Chat, ModelName = "gpt", TotalTokenCount = 10 },
        };

        var rows = AIAudioUsageReport.Build(records, AIAudioUsageReportGrouping.Model);

        Assert.Equal(2, rows.Count);

        var stt = Assert.Single(rows, row => row.OperationType == AIUsageOperationTypes.SpeechToText);
        Assert.Equal(2, stt.RequestCount);
        Assert.Equal(5_400_000, stt.AudioDurationMs);
        Assert.Equal(1.5, stt.AudioHours);
        Assert.Equal(4_800_000, stt.SpeechDurationMs);
        Assert.Equal(1_500, stt.AudioInputBytes);
        Assert.Equal(6, stt.SegmentCount);

        var tts = Assert.Single(rows, row => row.OperationType == AIUsageOperationTypes.TextToSpeech);
        Assert.Equal(120, tts.CharacterCount);
        Assert.Equal(2_000, tts.AudioOutputBytes);
    }

    [Fact]
    public void Build_ByVoice_KeepsVoicesApart()
    {
        var rows = AIAudioUsageReport.Build(
            [Tts("tts-1", 10, 1_000, 100, "alloy"), Tts("tts-1", 20, 1_000, 100, "nova"), Tts("tts-1", 5, 0, 0, null)],
            AIAudioUsageReportGrouping.ModelAndVoice);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.VoiceId == "alloy" && row.CharacterCount == 10);
        Assert.Contains(rows, row => row.VoiceId == "nova" && row.CharacterCount == 20);
        Assert.Contains(rows, row => row.VoiceId == AIUsageReport.Unspecified);
    }

    [Fact]
    public void Build_CountsHowManyRequestsReportedALength()
    {
        var rows = AIAudioUsageReport.Build(
            [Tts("tts-1", 10, 1_000, 100, "alloy"), Tts("tts-1", 10, 0, 100, "alloy")],
            AIAudioUsageReportGrouping.Model);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.RequestCount);
        Assert.Equal(1, row.RequestsWithDurationCount);
    }

    [Fact]
    public void Wav_DurationIsReadFromTheHeader()
    {
        // 16-bit mono at 8 kHz is 16,000 bytes a second, so 32,000 bytes of audio play for two seconds.
        var audio = Wav(byteRate: 16_000, dataLength: 32_000);

        var measured = AIGeneratedAudio.Measure([new DataContent(audio, "audio/wav")]);

        Assert.Equal(audio.Length, measured.Bytes);
        Assert.Equal(2_000, measured.DurationMs);
        Assert.Equal("audio/wav", measured.MediaType);
    }

    [Fact]
    public void NonWavAudio_HasASizeButNoDuration()
    {
        var measured = AIGeneratedAudio.Measure([new DataContent(new byte[] { 1, 2, 3 }, "audio/mpeg")]);

        Assert.Equal(3, measured.Bytes);
        Assert.Equal(0, measured.DurationMs);
    }

    [Fact]
    public void ARiffFileWithAHugeChunk_DoesNotLoop()
    {
        var bytes = new List<byte>();
        bytes.AddRange("RIFF"u8.ToArray());
        bytes.AddRange(new byte[4]);
        bytes.AddRange("WAVE"u8.ToArray());
        bytes.AddRange("junk"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes(uint.MaxValue - 1));

        Assert.False(AIGeneratedAudio.TryGetWavDurationMs(bytes.ToArray(), out _));
    }

    private static byte[] Wav(int byteRate, int dataLength)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8_000);
        writer.Write(byteRate);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);

        return stream.ToArray();
    }

    private static AICompletionUsageRecord Stt(string model, long audioDurationMs, long audioStartMs, long inputBytes, int segments)
    {
        return new AICompletionUsageRecord
        {
            OperationType = AIUsageOperationTypes.SpeechToText,
            ClientName = "Provider",
            ModelName = model,
            AudioDurationMs = audioDurationMs,
            AudioStartMs = audioStartMs,
            AudioInputBytes = inputBytes,
            SegmentCount = segments,
        };
    }

    private static AICompletionUsageRecord Tts(string model, int characters, long durationMs, long outputBytes, string voice)
    {
        return new AICompletionUsageRecord
        {
            OperationType = AIUsageOperationTypes.TextToSpeech,
            ClientName = "Provider",
            ModelName = model,
            CharacterCount = characters,
            AudioDurationMs = durationMs,
            AudioOutputBytes = outputBytes,
            VoiceId = voice,
        };
    }
}
