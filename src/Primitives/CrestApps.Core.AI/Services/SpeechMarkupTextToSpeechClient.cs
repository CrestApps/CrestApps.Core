using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Renders the neutral <see cref="SpeechMarkup"/> in every text sent through a text-to-speech client for the provider
/// behind it. Providers with their own markup (ElevenLabs, Cartesia) render it themselves; wrap a provider with none in
/// <see cref="SpeechMarkupDialect.Plain"/> so a pause or a spelled-out code is never read aloud as a tag.
/// </summary>
public sealed class SpeechMarkupTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly SpeechMarkupDialect _dialect;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpeechMarkupTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="dialect">The markup the inner client understands.</param>
    public SpeechMarkupTextToSpeechClient(ITextToSpeechClient innerClient, SpeechMarkupDialect dialect = SpeechMarkupDialect.Plain)
        : base(innerClient)
    {
        _dialect = dialect;
    }

    /// <inheritdoc />
    public override Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(SpeechMarkup.Render(text, _dialect), options, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(SpeechMarkup.Render(text, _dialect), options, cancellationToken);
    }
}
#pragma warning restore MEAI001
