#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.ElevenLabs.Services;

/// <summary>
/// The "ElevenLabs" provider: speech services only. A deployment's model names the text-to-speech model (for example
/// <c>eleven_flash_v2_5</c>) or the speech-to-text model (<c>scribe_v1</c>); its connection holds the API key and,
/// optionally, a regional endpoint.
/// </summary>
public sealed class ElevenLabsClientProvider : IAIClientProvider, IVoiceLibraryProvider
{
    private const int VoiceListSize = 100;

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevenLabsClientProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public ElevenLabsClientProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public bool CanHandle(string clientName)
        => string.Equals(ElevenLabsConstants.ClientName, clientName, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<ITextToSpeechClient> GetTextToSpeechClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => ValueTask.FromResult<ITextToSpeechClient>(new ElevenLabsTextToSpeechClient(CreateApi(connection), deploymentName));

    /// <inheritdoc />
    public ValueTask<ISpeechToTextClient> GetSpeechToTextClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => ValueTask.FromResult<ISpeechToTextClient>(new ElevenLabsSpeechToTextClient(CreateApi(connection), deploymentName));

    /// <inheritdoc />
    public async Task<SpeechVoice[]> GetSpeechVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        var result = await GetLibrary(connection).SearchAsync(new VoiceSearchRequest { PageSize = VoiceListSize });

        return [.. result.Voices];
    }

    /// <inheritdoc />
    public IVoiceLibrary GetLibrary(AIProviderConnectionEntry connection)
        => new ElevenLabsVoiceLibrary(CreateApi(connection));

    /// <inheritdoc />
    public ValueTask<IChatClient> GetChatClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("ElevenLabs deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> GetEmbeddingGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("ElevenLabs deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IImageGenerator> GetImageGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("ElevenLabs deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IRealtimeClient> GetRealtimeClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("ElevenLabs deployments only support speech services. Use it as the text-to-speech and speech-to-text legs of a cascaded realtime deployment.");

    /// <inheritdoc />
    public Task<SpeechVoice[]> GetRealtimeVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => Task.FromResult(Array.Empty<SpeechVoice>());

    private ElevenLabsApi CreateApi(AIProviderConnectionEntry connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new ElevenLabsApi(
            _httpClientFactory.CreateClient(ElevenLabsConstants.HttpClientName),
            connection.GetEndpoint(throwException: false),
            connection.GetApiKey());
    }
}
