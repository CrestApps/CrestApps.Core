#pragma warning disable MEAI001
using CrestApps.Core.AI.AzureAIInference.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Ollama.Services;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.AI.OpenAI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies the realtime capability of each registered <see cref="CrestApps.Core.AI.Clients.IAIClientProvider"/>:
/// OpenAI and Azure OpenAI construct a real <see cref="IRealtimeClient"/> from the connection, while
/// providers without a realtime API (Ollama, Azure AI Inference, Azure Speech) surface <see cref="NotSupportedException"/>.
/// </summary>
public sealed class AIClientProviderRealtimeTests
{
    private static ServiceProvider EmptyServices()
        => new ServiceCollection().BuildServiceProvider();

    private static AIProviderConnectionEntry Connection(params (string Key, string Value)[] values)
        => new(values.ToDictionary(pair => pair.Key, object (pair) => pair.Value));

    [Fact]
    public async Task OpenAI_GetRealtimeClientAsync_UsesConnectionRealtimeDeployment_ReturnsClient()
    {
        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"), ("RealtimeDeploymentName", "gpt-4o-realtime-preview"));

        var client = await provider.GetRealtimeClientAsync(connection);

        Assert.NotNull(client);
        Assert.IsType<OpenAIRealtimeClient>(client);
    }

    [Fact]
    public async Task OpenAI_GetRealtimeClientAsync_UsesExplicitDeployment_ReturnsClient()
    {
        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"));

        var client = await provider.GetRealtimeClientAsync(connection, "gpt-4o-realtime-preview");

        Assert.NotNull(client);
    }

    [Fact]
    public async Task OpenAI_GetRealtimeClientAsync_WhenNoDeploymentName_ThrowsArgumentException()
    {
        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"));

        await Assert.ThrowsAsync<ArgumentException>(async () => await provider.GetRealtimeClientAsync(connection));
    }

    [Fact]
    public async Task AzureOpenAI_GetRealtimeClientAsync_ReturnsClient()
    {
        // Azure OpenAI realtime uses the temporary WebSocket transport (CrestApps.Core.AI.OpenAI.Azure.Realtime)
        // rather than the SDK, because the pinned Azure.AI.OpenAI is version-incompatible with the OpenAI SDK
        // required by Microsoft.Extensions.AI.OpenAI.
        var options = Mock.Of<IOptionsSnapshot<AzureClientOptions>>(snapshot => snapshot.Value == new AzureClientOptions());
        var provider = new AzureOpenAIClientProvider(EmptyServices(), NullLoggerFactory.Instance, options);
        var connection = Connection(
            ("Endpoint", "https://unit-test.openai.azure.com/"),
            ("ApiKey", "azure-test-key"),
            ("AuthenticationType", "ApiKey"),
            ("RealtimeDeploymentName", "gpt-4o-realtime-preview"));

        var client = await provider.GetRealtimeClientAsync(connection);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task AzureOpenAI_GetRealtimeClientAsync_WhenNoDeploymentName_ThrowsArgumentException()
    {
        var options = Mock.Of<IOptionsSnapshot<AzureClientOptions>>(snapshot => snapshot.Value == new AzureClientOptions());
        var provider = new AzureOpenAIClientProvider(EmptyServices(), NullLoggerFactory.Instance, options);
        var connection = Connection(
            ("Endpoint", "https://unit-test.openai.azure.com/"),
            ("ApiKey", "azure-test-key"),
            ("AuthenticationType", "ApiKey"));

        await Assert.ThrowsAsync<ArgumentException>(async () => await provider.GetRealtimeClientAsync(connection));
    }

    [Fact]
    public async Task Ollama_GetRealtimeClientAsync_ThrowsNotSupported()
    {
        var provider = new OllamaAIClientProvider(EmptyServices());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetRealtimeClientAsync(Connection()));
    }

    [Fact]
    public async Task AzureAIInference_GetRealtimeClientAsync_ThrowsNotSupported()
    {
        var provider = new AzureAIInferenceClientProvider(EmptyServices());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetRealtimeClientAsync(Connection()));
    }

    [Fact]
    public async Task AzureSpeech_GetRealtimeClientAsync_ThrowsNotSupported()
    {
        var provider = new AzureSpeechClientProvider(NullLoggerFactory.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetRealtimeClientAsync(Connection()));
    }

    [Fact]
    public async Task OpenAI_GetRealtimeVoicesAsync_ReturnsProviderVoiceSet()
    {
        var provider = new OpenAIClientProvider(EmptyServices());

        var voices = await provider.GetRealtimeVoicesAsync(Connection());

        Assert.Contains(voices, voice => voice.Id == "alloy");
        Assert.Contains(voices, voice => voice.Id == "cedar");
        Assert.Contains(voices, voice => voice.Id == "marin");
        Assert.Contains(voices, voice => voice.Id == "coral" && voice.Gender == SpeechVoiceGender.Female);
    }

    [Fact]
    public async Task AzureOpenAI_GetRealtimeVoicesAsync_ReturnsProviderVoiceSet()
    {
        var options = Mock.Of<IOptionsSnapshot<AzureClientOptions>>(snapshot => snapshot.Value == new AzureClientOptions());
        var provider = new AzureOpenAIClientProvider(EmptyServices(), NullLoggerFactory.Instance, options);

        Assert.Contains(await provider.GetRealtimeVoicesAsync(Connection()), voice => voice.Id == "alloy");
    }

    [Fact]
    public async Task Ollama_GetRealtimeVoicesAsync_ReturnsEmpty()
    {
        var provider = new OllamaAIClientProvider(EmptyServices());

        Assert.Empty(await provider.GetRealtimeVoicesAsync(Connection()));
    }
}
