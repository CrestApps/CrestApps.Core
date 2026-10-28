using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Applies a <see cref="PronunciationDictionary"/> to every text sent through a text-to-speech client, so the
/// dictionary works with every provider.
/// </summary>
public sealed class PronunciationTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly PronunciationDictionary _dictionary;

    /// <summary>
    /// Initializes a new instance of the <see cref="PronunciationTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="dictionary">The dictionary to apply.</param>
    public PronunciationTextToSpeechClient(ITextToSpeechClient innerClient, PronunciationDictionary dictionary)
        : base(innerClient)
    {
        _dictionary = dictionary ?? PronunciationDictionary.Empty;
    }

    /// <inheritdoc />
    public override Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(_dictionary.Apply(text), options, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(_dictionary.Apply(text), options, cancellationToken);
    }
}
#pragma warning restore MEAI001
