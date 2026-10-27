using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // These client types are for evaluation purposes only and may change in future updates.
/// <summary>
/// Applies a profile's speaking rate to every synthesis sent through a text-to-speech client.
/// </summary>
internal sealed class SpeakingRateTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly AIProfileSpeechSettings _settings;

    public SpeakingRateTextToSpeechClient(ITextToSpeechClient innerClient, AIProfileSpeechSettings settings)
        : base(innerClient)
    {
        _settings = settings;
    }

    public override Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(text, WithRate(options), cancellationToken);
    }

    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(text, WithRate(options), cancellationToken);
    }

    private TextToSpeechOptions WithRate(TextToSpeechOptions options)
    {
        var clone = options?.Clone() ?? new TextToSpeechOptions();
        ProfileSpeech.ApplySpeakingRate(clone, _settings);

        return clone;
    }
}

/// <summary>
/// Passes a profile's boosted keywords and recognition languages to every transcription sent through a
/// speech-to-text client. A provider that cannot use them ignores the extra properties.
/// </summary>
internal sealed class ProfileSpeechToTextClient : DelegatingSpeechToTextClient
{
    private readonly AIProfileSpeechSettings _settings;

    public ProfileSpeechToTextClient(ISpeechToTextClient innerClient, AIProfileSpeechSettings settings)
        : base(innerClient)
    {
        _settings = settings;
    }

    public override Task<SpeechToTextResponse> GetTextAsync(Stream audioSpeechStream, SpeechToTextOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetTextAsync(audioSpeechStream, WithRecognition(options), cancellationToken);
    }

    public override IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(Stream audioSpeechStream, SpeechToTextOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingTextAsync(audioSpeechStream, WithRecognition(options), cancellationToken);
    }

    private SpeechToTextOptions WithRecognition(SpeechToTextOptions options)
    {
        var clone = options?.Clone() ?? new SpeechToTextOptions();
        ProfileSpeech.ApplyRecognition(clone, _settings);

        return clone;
    }
}
#pragma warning restore MEAI001
