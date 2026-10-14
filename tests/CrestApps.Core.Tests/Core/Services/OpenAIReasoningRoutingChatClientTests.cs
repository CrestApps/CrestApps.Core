#pragma warning disable OPENAI001
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI.Responses;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies that chat requests combining function tools with a reasoning effort are sent to the Responses API,
/// which reasoning models such as gpt-6-luna require, while every other request stays on Chat Completions.
/// </summary>
[Collection(AzureOpenAIClientCacheCollection.Name)]
public sealed class OpenAIReasoningRoutingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_WhenToolsAndReasoningEffort_ShouldUseResponsesClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(responses.Called);
        Assert.False(chatCompletions.Called);
        Assert.Equal(options.Tools, responses.LastOptions.Tools);
        Assert.Equal(ReasoningEffort.Medium, responses.LastOptions.Reasoning.Effort);
    }

    [Fact]
    public async Task GetResponseAsync_WhenRoutedToResponses_ShouldNotStoreTheConversation()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        var createOptions = Assert.IsType<CreateResponseOptions>(responses.LastOptions.RawRepresentationFactory(responses));
        Assert.False(createOptions.StoredOutputEnabled);
        Assert.Contains(IncludedResponseProperty.ReasoningEncryptedContent, createOptions.IncludedProperties);
        Assert.Null(options.RawRepresentationFactory);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenToolsAndReasoningEffort_ShouldUseResponsesClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
        };

        // Act
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        Assert.True(responses.Called);
        Assert.False(chatCompletions.Called);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningEffortIsNone_ShouldUseChatCompletionsClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(chatCompletions.Called);
        Assert.False(responses.Called);
    }

    [Fact]
    public async Task GetResponseAsync_WhenToolsWithoutReasoning_ShouldUseChatCompletionsClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(chatCompletions.Called);
        Assert.False(responses.Called);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningWithoutTools_ShouldUseChatCompletionsClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(chatCompletions.Called);
        Assert.False(responses.Called);
    }

    [Fact]
    public async Task GetResponseAsync_WhenOptionsAreNull_ShouldUseChatCompletionsClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        using var client = new OpenAIReasoningRoutingChatClient(chatCompletions, responses);

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(chatCompletions.Called);
        Assert.False(responses.Called);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningDeploymentReceivesToolsAndEffort_ShouldUseResponsesClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Reasoning);
        using var client = CreateEnforcingClient(new OpenAIReasoningRoutingChatClient(chatCompletions, responses), deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(responses.Called);
        Assert.False(chatCompletions.Called);
        Assert.Equal(ReasoningEffort.Medium, responses.LastOptions.Reasoning.Effort);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDeploymentDoesNotDeclareReasoning_ShouldUseChatCompletionsClient()
    {
        // Arrange
        var chatCompletions = new CapturingChatClient();
        var responses = new CapturingChatClient();
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling);
        using var client = CreateEnforcingClient(new OpenAIReasoningRoutingChatClient(chatCompletions, responses), deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(chatCompletions.Called);
        Assert.False(responses.Called);
        Assert.Null(chatCompletions.LastOptions.Reasoning);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("https://api.openai.com/v1/", null, true)]
    [InlineData("https://eu.api.openai.com/v1/", null, true)]
    [InlineData("https://api.openai.com.example.com/v1/", null, false)]
    [InlineData("https://api.groq.com/openai/v1", "NotAnApi", false)]
    [InlineData(null, "chatcompletions", false)]
    [InlineData("https://api.groq.com/openai/v1", null, false)]
    [InlineData("https://api.groq.com/openai/v1", "Auto", false)]
    [InlineData(null, "ChatCompletions", false)]
    [InlineData("https://openrouter.ai/api/v1", "Responses", true)]
    public async Task OpenAI_GetChatClientAsync_RoutesOnlyOnOpenAIEndpointUnlessChatApiIsSet(string endpoint, string chatApi, bool expectRouting)
    {
        // Arrange
        var provider = new OpenAIClientProvider(new ServiceCollection().BuildServiceProvider());
        var values = new Dictionary<string, object>
        {
            ["ApiKey"] = "sk-test-key",
        };

        if (endpoint is not null)
        {
            values["Endpoint"] = endpoint;
        }

        if (chatApi is not null)
        {
            values[OpenAIChatApis.SettingName] = chatApi;
        }

        // Act
        var client = await provider.GetChatClientAsync(new AIProviderConnectionEntry(values), "gpt-6-luna");

        // Assert
        Assert.Equal(expectRouting, client.GetService<OpenAIReasoningRoutingChatClient>() is not null);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Auto", true)]
    [InlineData("Responses", true)]
    [InlineData("ChatCompletions", false)]
    public async Task AzureOpenAI_GetChatClientAsync_RoutesUnlessChatApiIsChatCompletions(string chatApi, bool expectRouting)
    {
        // Arrange
        // AzureOpenAIClient.GetResponsesClient() throws MissingMethodException with the pinned Azure.AI.OpenAI, so
        // creating the chat client also guards against routing the Responses client back through the SDK too early.
        var options = Mock.Of<IOptionsSnapshot<AzureClientOptions>>(snapshot => snapshot.Value == new AzureClientOptions());
        var provider = new AzureOpenAIClientProvider(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, options);
        var values = new Dictionary<string, object>
        {
            ["Endpoint"] = "https://unit-test.openai.azure.com/",
            ["ApiKey"] = "azure-test-key",
            ["AuthenticationType"] = "ApiKey",
        };

        if (chatApi is not null)
        {
            values[OpenAIChatApis.SettingName] = chatApi;
        }

        // Act
        var client = await provider.GetChatClientAsync(new AIProviderConnectionEntry(values), "gpt-6-luna");

        // Assert
        Assert.Equal(expectRouting, client.GetService<OpenAIReasoningRoutingChatClient>() is not null);
    }

    private static CapabilityEnforcingChatClient CreateEnforcingClient(IChatClient inner, AIDeployment deployment)
    {
        var options = new AIDeploymentCapabilityOptions();
        options.AddFeature(AIDeploymentFeatureNames.ToolCalling, new LocalizedString("Tool calling", "Tool calling"));
        options.AddFeature(AIDeploymentFeatureNames.Reasoning, new LocalizedString("Reasoning", "Reasoning"));
        options.AddParameter(AIDeploymentParameterNames.ReasoningEffort, new LocalizedString("Reasoning effort", "Reasoning effort"), parameter =>
        {
            parameter.Kind = AIDeploymentParameterKind.Choice;
            parameter.RequiredFeature = AIDeploymentFeatureNames.Reasoning;
            parameter.DefaultValue = nameof(ReasoningEffort.None);
            parameter.AllowedValues =
            [
                new AIDeploymentParameterOption { Value = nameof(ReasoningEffort.None), DisplayName = new LocalizedString("None", "None") },
                new AIDeploymentParameterOption { Value = nameof(ReasoningEffort.Medium), DisplayName = new LocalizedString("Medium", "Medium") },
            ];
        });

        var service = new DefaultAIDeploymentCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());

        return new CapabilityEnforcingChatClient(inner, deployment, service, NullLogger<CapabilityEnforcingChatClient>.Instance);
    }

    private static AIDeployment CreateDeployment(params string[] features)
    {
        var deployment = new AIDeployment
        {
            Name = "gpt-6-luna",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = features,
            Parameters = new(StringComparer.OrdinalIgnoreCase)
            {
                [AIDeploymentParameterNames.ReasoningEffort] = new AIDeploymentParameter(),
            },
        });

        return deployment;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions LastOptions { get; private set; }

        public bool Called { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            LastOptions = options;


            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Called = true;
            LastOptions = options;


            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");

            await Task.CompletedTask;
        }

        public object GetService(Type serviceType, object serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class TestAIFunction : AIFunction
    {
        public TestAIFunction(string name)
        {
            Name = name;
        }

        public override string Name { get; }

        public override string Description => Name;

        public override System.Text.Json.JsonElement JsonSchema
            => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return new ValueTask<object>(Name);
        }
    }

}