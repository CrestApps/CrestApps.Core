#pragma warning disable MEAI001 // The speech APIs from Microsoft.Extensions.AI are for evaluation purposes only.
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class ProfileSpeechTests
{
    [Fact]
    public void PronunciationDictionary_ReplacesWholeWordsOnly()
    {
        var dictionary = PronunciationDictionary.Create(
        [
            new PronunciationEntry { Text = "SQL", Alias = "sequel" },
            new PronunciationEntry { Text = "Dr.", Alias = "Doctor" },
        ]);

        Assert.Equal("Use sequel or MySQL, said Doctor Lee.", dictionary.Apply("Use SQL or MySQL, said Dr. Lee."));
    }

    [Fact]
    public void PronunciationDictionary_PrefersTheLongerPhrase()
    {
        var dictionary = PronunciationDictionary.Create(
        [
            new PronunciationEntry { Text = "AI", Alias = "A I" },
            new PronunciationEntry { Text = "AI Studio", Alias = "Studio" },
        ]);

        Assert.Equal("Studio and A I", dictionary.Apply("AI Studio and AI"));
    }

    [Fact]
    public void PronunciationDictionary_HonorsMatchCase()
    {
        var dictionary = PronunciationDictionary.Create([new PronunciationEntry { Text = "Ok", Alias = "okay", MatchCase = true }]);

        Assert.Equal("okay ok", dictionary.Apply("Ok ok"));
    }

    [Fact]
    public void PronunciationDictionary_TreatsAliasAsLiteralText()
    {
        var dictionary = PronunciationDictionary.Create([new PronunciationEntry { Text = "USD", Alias = "$1 dollars" }]);

        Assert.Equal("5 $1 dollars", dictionary.Apply("5 USD"));
    }

    [Fact]
    public void PronunciationDictionary_WithNoUsableEntries_IsEmpty()
    {
        Assert.True(PronunciationDictionary.Create(null).IsEmpty);
        Assert.True(PronunciationDictionary.Create([new PronunciationEntry { Text = " ", Alias = "x" }, new PronunciationEntry { Text = "a" }]).IsEmpty);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0d, null)]
    [InlineData(-1d, null)]
    [InlineData(1.25, 1.25)]
    [InlineData(10d, 4d)]
    [InlineData(0.01, 0.25)]
    public void NormalizeSpeakingRate_ClampsOrIgnores(double? input, double? expected)
    {
        Assert.Equal(expected, ProfileSpeech.NormalizeSpeakingRate(input));
    }

    [Fact]
    public void ApplySpeakingRate_MultipliesAnExistingPace()
    {
        var options = new TextToSpeechOptions { Speed = 1.5f };

        ProfileSpeech.ApplySpeakingRate(options, new AIProfileSpeechSettings { SpeakingRate = 2 });

        Assert.Equal(3f, options.Speed);
    }

    [Fact]
    public void ApplySpeakingRate_PassesTheProfilesEmotionToTheProvider()
    {
        var options = new TextToSpeechOptions();

        ProfileSpeech.ApplySpeakingRate(options, new AIProfileSpeechSettings { SpeakingEmotion = " calm " });

        Assert.Equal("calm", options.AdditionalProperties![TextToSpeechOptionsProperties.Emotion]);

        var untouched = new TextToSpeechOptions();
        ProfileSpeech.ApplySpeakingRate(untouched, new AIProfileSpeechSettings());

        Assert.Null(untouched.AdditionalProperties);
    }

    [Fact]
    public void ApplyRecognition_AddsKeywordsAndKeepsAnExplicitLanguage()
    {
        var options = new SpeechToTextOptions { SpeechLanguage = "fr-FR" };

        ProfileSpeech.ApplyRecognition(options, new AIProfileSpeechSettings
        {
            BoostedKeywords = ["Contoso", " ", "contoso", "Fabrikam"],
            RecognitionLanguages = ["es-MX", "en-US"],
        });

        Assert.Equal("fr-FR", options.SpeechLanguage);
        Assert.Equal(["Contoso", "Fabrikam"], (IReadOnlyList<string>)options.AdditionalProperties[SpeechToTextOptionsProperties.BoostedKeywords]);
        Assert.Equal(["es-MX", "en-US"], (IReadOnlyList<string>)options.AdditionalProperties[SpeechToTextOptionsProperties.RecognitionLanguages]);
    }

    [Fact]
    public void ApplyRecognition_UsesThePrimaryLanguageWhenNoneIsSet()
    {
        var options = new SpeechToTextOptions();

        ProfileSpeech.ApplyRecognition(options, new AIProfileSpeechSettings { RecognitionLanguages = ["es-MX"] });

        Assert.Equal("es-MX", options.SpeechLanguage);
    }

    [Fact]
    public async Task PronunciationClient_RewritesTheTextBeforeSynthesis()
    {
        var inner = new RecordingSpeechClient();
        using var client = new PronunciationTextToSpeechClient(inner, PronunciationDictionary.Create([new PronunciationEntry { Text = "SQL", Alias = "sequel" }]));

        await foreach (var _ in client.GetStreamingAudioAsync("SQL rocks", cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("sequel rocks", Assert.Single(inner.Texts));
    }

    [Fact]
    public async Task FallbackClient_UsesTheFallbackWhenThePrimaryFailsBeforeAudio()
    {
        var primary = new RecordingSpeechClient { FailBeforeAudio = true };
        var fallback = new RecordingSpeechClient();
        using var client = new FallbackTextToSpeechClient(primary, fallback);

        var updates = new List<TextToSpeechResponseUpdate>();
        await foreach (var update in client.GetStreamingAudioAsync("hi", new TextToSpeechOptions { VoiceId = "primary-voice", ModelId = "m" }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        Assert.Equal(["hi"], fallback.Texts);

        // A voice id and model id belong to one provider, so the fallback speaks with its own defaults.
        Assert.Null(fallback.Options.Single().VoiceId);
        Assert.Null(fallback.Options.Single().ModelId);
    }

    [Fact]
    public async Task FallbackClient_DoesNotSpliceVoicesAfterAudioHasStarted()
    {
        var primary = new RecordingSpeechClient { FailAfterAudio = true };
        var fallback = new RecordingSpeechClient();
        using var client = new FallbackTextToSpeechClient(primary, fallback);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.GetStreamingAudioAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Empty(fallback.Texts);
    }

    [Fact]
    public async Task FallbackClient_PropagatesCancellationWithoutFallingBack()
    {
        var primary = new RecordingSpeechClient { CancelBeforeAudio = true };
        var fallback = new RecordingSpeechClient();
        using var client = new FallbackTextToSpeechClient(primary, fallback);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.GetStreamingAudioAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Empty(fallback.Texts);
    }

    [Fact]
    public async Task FallbackClient_GetAudioAsync_FallsBack()
    {
        var primary = new RecordingSpeechClient { FailBeforeAudio = true };
        var fallback = new RecordingSpeechClient();
        using var client = new FallbackTextToSpeechClient(primary, fallback);

        await client.GetAudioAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["hi"], fallback.Texts);
    }

    private sealed class RecordingSpeechClient : ITextToSpeechClient
    {
        public List<string> Texts { get; } = [];

        public List<TextToSpeechOptions> Options { get; } = [];

        public bool FailBeforeAudio { get; set; }

        public bool FailAfterAudio { get; set; }

        public bool CancelBeforeAudio { get; set; }

        public Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
        {
            Texts.Add(text);
            Options.Add(options);

            return FailBeforeAudio
                ? throw new InvalidOperationException("primary failed")
                : Task.FromResult(new TextToSpeechResponse());
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Texts.Add(text);
            Options.Add(options);

            await Task.Yield();

            if (CancelBeforeAudio)
            {
                throw new OperationCanceledException();
            }

            if (FailBeforeAudio)
            {
                throw new InvalidOperationException("primary failed");
            }

            yield return new TextToSpeechResponseUpdate([new DataContent(new byte[] { 1, 2 }, "audio/mp3")]);

            if (FailAfterAudio)
            {
                throw new InvalidOperationException("primary failed midway");
            }
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }
    }
}
