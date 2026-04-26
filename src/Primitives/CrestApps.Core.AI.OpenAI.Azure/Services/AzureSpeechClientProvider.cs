using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// Client provider for "AzureSpeech" deployments.
/// Uses the Azure Speech SDK for speech-to-text and text-to-speech.
/// </summary>
public sealed class AzureSpeechClientProvider : IAIClientProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureSpeechClientProvider"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AzureSpeechClientProvider(
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    public bool CanHandle(string providerName)
    {
        return string.Equals(AzureOpenAIConstants.AzureSpeechClientName, providerName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IChatClient> GetChatClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        throw new NotSupportedException("Azure AI Speech deployments only support speech services.");
    }

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> GetEmbeddingGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        throw new NotSupportedException("Azure AI Speech deployments only support speech services.");
    }
#pragma warning disable MEAI001

    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IImageGenerator> GetImageGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        throw new NotSupportedException("Azure AI Speech deployments only support speech services.");
    }
#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.

    /// <summary>
    /// Gets speech to text client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<ISpeechToTextClient> GetSpeechToTextClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        var (endpoint, authType, apiKey, identityId) = ExtractConnectionParams(connection);
        var logger = _loggerFactory.CreateLogger<AzureSpeechServiceSpeechToTextClient>();
        var client = new AzureSpeechServiceSpeechToTextClient(endpoint, authType, apiKey, identityId, _timeProvider, logger);
        return ValueTask.FromResult<ISpeechToTextClient>(client);
    }
#pragma warning restore MEAI001
#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.

    /// <summary>
    /// Gets text to speech client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<ITextToSpeechClient> GetTextToSpeechClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        var (endpoint, authType, apiKey, identityId) = ExtractConnectionParams(connection);
        var logger = _loggerFactory.CreateLogger<AzureSpeechServiceTextToSpeechClient>();
        var client = new AzureSpeechServiceTextToSpeechClient(endpoint, authType, apiKey, identityId, _timeProvider, logger);
        return ValueTask.FromResult<ITextToSpeechClient>(client);
    }
#pragma warning restore MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.

    /// <summary>
    /// Gets speech voices.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public async Task<SpeechVoice[]> GetSpeechVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        var (endpoint, authType, apiKey, identityId) = ExtractConnectionParams(connection);
        var logger = _loggerFactory.CreateLogger<AzureSpeechServiceTextToSpeechClient>();
        using var client = new AzureSpeechServiceTextToSpeechClient(endpoint, authType, apiKey, identityId, _timeProvider, logger);
        return await client.GetVoicesAsync();
    }

    private static (Uri endpoint, AzureAuthenticationType authType, string apiKey, string identityId) ExtractConnectionParams(AIProviderConnectionEntry connection)
    {
        var endpoint = connection.GetEndpoint();
        var authType = connection.GetAzureAuthenticationType();
        var apiKey = authType == AzureAuthenticationType.ApiKey ? connection.GetApiKey() : null;
        var identityId = connection.GetIdentityId();
        return (endpoint, authType, apiKey, identityId);
    }
}
