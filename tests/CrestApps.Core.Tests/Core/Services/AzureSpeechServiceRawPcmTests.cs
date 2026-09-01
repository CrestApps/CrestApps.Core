using CrestApps.Core.AI.OpenAI.Azure.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AzureSpeechServiceRawPcmTests
{
    [Theory]
    // Raw PCM hints (what the browser mic path now sends) -> recognized, with the sample rate parsed.
    [InlineData("audio/pcm;rate=16000", true, 16000)]
    [InlineData("audio/pcm", true, 16000)]           // defaults to 16 kHz
    [InlineData("audio/pcm;rate=8000", true, 8000)]
    [InlineData("audio/l16;rate=24000", true, 24000)]
    [InlineData("AUDIO/PCM;RATE=16000", true, 16000)] // case-insensitive
    [InlineData("audio/pcm;rate=abc", true, 16000)]   // unparseable rate falls back to default
    [InlineData("audio/pcm;rate=0", true, 16000)]     // non-positive rate ignored
    // Compressed containers are NOT raw PCM and must fall through to the GStreamer path.
    [InlineData("audio/webm;codecs=opus", false, 16000)]
    [InlineData("audio/ogg;codecs=opus", false, 16000)]
    [InlineData("audio/mp3", false, 16000)]
    [InlineData("", false, 16000)]
    [InlineData(null, false, 16000)]
    public void TryGetRawPcmSampleRate_DetectsPcmAndParsesRate(string format, bool expectedIsPcm, int expectedRate)
    {
        var isPcm = AzureSpeechServiceSpeechToTextClient.TryGetRawPcmSampleRate(format, out var sampleRate);

        Assert.Equal(expectedIsPcm, isPcm);
        Assert.Equal(expectedRate, sampleRate);
    }
}
