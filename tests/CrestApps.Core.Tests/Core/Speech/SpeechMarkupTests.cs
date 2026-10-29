#nullable enable
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class SpeechMarkupTests
{
    [Theory]
    [InlineData("a<break time=\"500ms\"/>b", SpeechMarkupDialect.ElevenLabs, "a<break time=\"0.5s\" />b")]
    [InlineData("a<break time='2s'/>b", SpeechMarkupDialect.Cartesia, "a<break time=\"2.0s\"/>b")]
    [InlineData("a<break time=\"9s\"/>b", SpeechMarkupDialect.Cartesia, "a<break time=\"3.0s\"/>b")]
    [InlineData("a<break time=\"200ms\"/>b", SpeechMarkupDialect.Plain, "a, b")]
    [InlineData("a<break time=\"1.5s\"/>b", SpeechMarkupDialect.Plain, "a... b")]
    [InlineData("<spell>AB 12</spell>", SpeechMarkupDialect.Cartesia, "<spell>AB12</spell>")]
    [InlineData("<spell>AB 12</spell>", SpeechMarkupDialect.ElevenLabs, "A-B-1-2")]
    [InlineData("<spell>AB 12</spell>", SpeechMarkupDialect.Plain, "A B 1 2")]
    public void Render_TurnsNeutralMarkupIntoWhatTheProviderUnderstands(string text, SpeechMarkupDialect dialect, string expected)
        => Assert.Equal(expected, SpeechMarkup.Render(text, dialect));

    [Theory]
    [InlineData("Plain text, 5 < 6 and 7 > 3.")]
    [InlineData("")]
    [InlineData(null)]
    public void Render_LeavesTextWithoutMarkupAlone(string? text)
    {
        Assert.False(SpeechMarkup.HasMarkup(text!));
        Assert.Equal(text, SpeechMarkup.Render(text!, SpeechMarkupDialect.Cartesia));
    }

    [Fact]
    public void Strip_RemovesMarkupSoItIsNeverReadAloud()
        => Assert.DoesNotContain("<", SpeechMarkup.Strip("Call <spell>X9</spell><break time=\"1s\"/> now"));

    [Theory]
    [InlineData("Pcm24000", true, 24000)]
    [InlineData("pcm8000", true, 8000)]
    [InlineData("Pcm", false, 0)]
    [InlineData("Pcmx", false, 0)]
    [InlineData("mp3", false, 0)]
    [InlineData(null, false, 0)]
    public void SpeechAudioFormat_ReadsTheRequestedPcmRate(string? format, bool isPcm, int rate)
    {
        Assert.Equal(isPcm, SpeechAudioFormat.TryGetPcmSampleRate(format!, out var parsed));
        Assert.Equal(rate, parsed);
    }

    [Fact]
    public void ToUploadable_WrapsRawPcmInAWavHeader_AndPassesContainersThrough()
    {
        var wrapped = SpeechAudioFormat.ToUploadable([1, 2, 3, 4], 24000);

        Assert.Equal("audio.wav", wrapped.FileName);
        Assert.Equal(48, wrapped.Content.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wrapped.Content, 0, 4));
        Assert.Equal(24000, BitConverter.ToInt32(wrapped.Content, 24));
        Assert.Equal(4, BitConverter.ToInt32(wrapped.Content, 40));
        Assert.Equal(16000, BitConverter.ToInt32(SpeechAudioFormat.ToUploadable([1, 2], null).Content, 24));

        var mp3 = SpeechAudioFormat.ToUploadable([(byte)'I', (byte)'D', (byte)'3', 4], null);
        Assert.Equal(("audio/mpeg", "audio.mp3", 4), (mp3.ContentType, mp3.FileName, mp3.Content.Length));

        var wav = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0 };
        Assert.Same(wav, SpeechAudioFormat.ToUploadable(wav, 8000).Content);
    }

    [Fact]
    public async Task TheDecorator_RendersMarkupBeforeTheInnerClientSpeaksIt()
    {
#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview.
        var spoken = new List<string>();
        var inner = new Moq.Mock<Microsoft.Extensions.AI.ITextToSpeechClient>();
        inner
            .Setup(c => c.GetAudioAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<Microsoft.Extensions.AI.TextToSpeechOptions>(), Moq.It.IsAny<CancellationToken>()))
            .Returns((string text, Microsoft.Extensions.AI.TextToSpeechOptions? _, CancellationToken _) =>
            {
                spoken.Add(text);

                return Task.FromResult(new Microsoft.Extensions.AI.TextToSpeechResponse());
            });

        var client = new SpeechMarkupTextToSpeechClient(inner.Object);

        await client.GetAudioAsync("Code <spell>X9</spell> now", null, TestContext.Current.CancellationToken);
        await client.GetAudioAsync("Nothing special", null, TestContext.Current.CancellationToken);
#pragma warning restore MEAI001

        Assert.Equal(["Code X 9 now", "Nothing special"], spoken);
    }
}
