using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class CapabilityEnforcingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_WhenDeploymentDoesNotDeclareToolCalling_ShouldRemoveToolsBeforeCallingInner()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.StructuredOutputs);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
            ToolMode = ChatToolMode.RequireAny,
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(inner.LastOptions.Tools);
        Assert.Null(inner.LastOptions.ToolMode);
    }

    [Fact]
    public async Task GetResponseAsync_ShouldNotMutateTheCallerOptions()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.StructuredOutputs);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(options.Tools);
        Assert.Single(options.Tools);
        Assert.NotSame(options, inner.LastOptions);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDeploymentDeclaresToolCalling_ShouldKeepToolsAndReuseTheSameOptions()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(inner.LastOptions.Tools);
        Assert.Single(inner.LastOptions.Tools);
        Assert.Same(options, inner.LastOptions);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDeploymentDeclaresNoMetadata_ShouldNotEnforce()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Tools = [new TestAIFunction("sample-tool")],
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(inner.LastOptions.Tools);
        Assert.Same(options, inner.LastOptions);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenDeploymentDoesNotDeclareStructuredOutputs_ShouldRemoveJsonResponseFormat()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
        };

        // Act
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        Assert.Null(inner.LastOptions.ResponseFormat);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDeploymentDoesNotDeclareReasoning_ShouldRemoveReasoning()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(inner.LastOptions.Reasoning);
        Assert.NotNull(options.Reasoning);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningEffortIsNotSupported_ShouldCoerceToDeploymentDefault()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateReasoningDeployment();
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReasoningEffort.Medium, inner.LastOptions.Reasoning.Effort);
        Assert.Equal(ReasoningEffort.ExtraHigh, options.Reasoning.Effort);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningEffortIsSupported_ShouldKeepReasoning()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateReasoningDeployment();
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReasoningEffort.High, inner.LastOptions.Reasoning.Effort);
        Assert.Same(options, inner.LastOptions);
    }

    [Fact]
    public async Task GetResponseAsync_WhenReasoningEffortParameterIsNotExposed_ShouldRemoveEffortButKeepReasoning()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.Reasoning);
        var client = CreateClient(inner, deployment);
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
        };

        // Act
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(inner.LastOptions.Reasoning);
        Assert.Null(inner.LastOptions.Reasoning.Effort);
        Assert.Equal(ReasoningEffort.High, options.Reasoning.Effort);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenDeploymentDoesNotDeclareStreaming_ShouldBufferAsNonStreamingResponse()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var client = CreateClient(inner, deployment);
        var updates = new List<ChatResponseUpdate>();

        // Act
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options: null, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert
        Assert.True(inner.NonStreamingCalled);
        Assert.False(inner.StreamingCalled);
        Assert.Equal("ok", string.Concat(updates.Select(update => update.Text)));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenDeploymentDeclaresStreaming_ShouldStreamNormally()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = CreateDeployment(AIModelFeatureNames.Streaming);
        var client = CreateClient(inner, deployment);

        // Act
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options: null, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        Assert.True(inner.StreamingCalled);
        Assert.False(inner.NonStreamingCalled);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenDeploymentDeclaresNoMetadata_ShouldStreamNormally()
    {
        // Arrange
        var inner = new CapturingChatClient();
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };
        var client = CreateClient(inner, deployment);

        // Act
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options: null, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        Assert.True(inner.StreamingCalled);
        Assert.False(inner.NonStreamingCalled);
    }

    private static CapabilityEnforcingChatClient CreateClient(IChatClient inner, AIDeployment deployment)
    {
        var options = new AIModelCapabilityOptions();
        options.AddFeature(AIModelFeatureNames.ToolCalling, new LocalizedString("Tool calling", "Tool calling"));
        options.AddFeature(AIModelFeatureNames.StructuredOutputs, new LocalizedString("Structured outputs", "Structured outputs"));
        options.AddFeature(AIModelFeatureNames.Reasoning, new LocalizedString("Reasoning", "Reasoning"));
        options.AddFeature(AIModelFeatureNames.Streaming, new LocalizedString("Streaming", "Streaming"));
        options.AddParameter(AIModelParameterNames.ReasoningEffort, new LocalizedString("Reasoning effort", "Reasoning effort"), parameter =>
        {
            parameter.Kind = AIModelParameterKind.Choice;
            parameter.RequiredFeature = AIModelFeatureNames.Reasoning;
            parameter.DefaultValue = nameof(ReasoningEffort.Medium);
            parameter.AllowedValues =
            [
                new AIModelParameterOption { Value = nameof(ReasoningEffort.Low), DisplayName = new LocalizedString("Low", "Low") },
                new AIModelParameterOption { Value = nameof(ReasoningEffort.Medium), DisplayName = new LocalizedString("Medium", "Medium") },
                new AIModelParameterOption { Value = nameof(ReasoningEffort.High), DisplayName = new LocalizedString("High", "High") },
            ];
        });

        var service = new DefaultAIModelCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());

        return new CapabilityEnforcingChatClient(inner, deployment, service, NullLogger<CapabilityEnforcingChatClient>.Instance);
    }

    private static AIDeployment CreateDeployment(params string[] features)
    {
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentModelMetadata
        {
            Features = features,
        });

        return deployment;
    }

    private static AIDeployment CreateReasoningDeployment()
    {
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentModelMetadata
        {
            Features = [AIModelFeatureNames.Reasoning],
            Parameters = new(StringComparer.OrdinalIgnoreCase)
            {
                [AIModelParameterNames.ReasoningEffort] = new AIDeploymentModelParameter(),
            },
        });

        return deployment;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions LastOptions { get; private set; }

        public bool NonStreamingCalled { get; private set; }

        public bool StreamingCalled { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            CancellationToken cancellationToken = default)
        {
            NonStreamingCalled = true;
            LastOptions = options;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalled = true;
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
