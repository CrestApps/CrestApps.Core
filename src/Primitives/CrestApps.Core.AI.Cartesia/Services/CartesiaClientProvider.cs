#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// The "Cartesia" provider: speech services only. A deployment's model names the text-to-speech model (for example
/// <c>sonic-2</c>) or the speech-to-text model (<c>ink-whisper</c>); its connection holds the API key.
/// </summary>
public sealed class CartesiaClientProvider : IAIClientProvider, IVoiceLibraryProvider
{
    private const int VoiceListSize = 100;

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaClientProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public CartesiaClientProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public bool CanHandle(string clientName)
        => string.Equals(CartesiaConstants.ClientName, clientName, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<ITextToSpeechClient> GetTextToSpeechClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => ValueTask.FromResult<ITextToSpeechClient>(new CartesiaTextToSpeechClient(CreateApi(connection), deploymentName));

    /// <inheritdoc />
    public ValueTask<ISpeechToTextClient> GetSpeechToTextClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => ValueTask.FromResult<ISpeechToTextClient>(new CartesiaSpeechToTextClient(CreateApi(connection), deploymentName));

    /// <inheritdoc />
    public async Task<SpeechVoice[]> GetSpeechVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        var result = await GetLibrary(connection).SearchAsync(new VoiceSearchRequest { PageSize = VoiceListSize });

        return [.. result.Voices];
    }

    /// <inheritdoc />
    public IVoiceLibrary GetLibrary(AIProviderConnectionEntry connection)
        => new CartesiaVoiceLibrary(CreateApi(connection));

    /// <inheritdoc />
    public ValueTask<IChatClient> GetChatClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("Cartesia deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> GetEmbeddingGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("Cartesia deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IImageGenerator> GetImageGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("Cartesia deployments only support speech services.");

    /// <inheritdoc />
    public ValueTask<IRealtimeClient> GetRealtimeClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => throw new NotSupportedException("Cartesia deployments only support speech services. Use it as the text-to-speech and speech-to-text legs of a cascaded realtime deployment.");

    /// <inheritdoc />
    public Task<SpeechVoice[]> GetRealtimeVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
        => Task.FromResult(Array.Empty<SpeechVoice>());

    private CartesiaApi CreateApi(AIProviderConnectionEntry connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new CartesiaApi(
            _httpClientFactory.CreateClient(CartesiaConstants.HttpClientName),
            connection.GetEndpoint(throwException: false),
            connection.GetApiKey());
    }
}
