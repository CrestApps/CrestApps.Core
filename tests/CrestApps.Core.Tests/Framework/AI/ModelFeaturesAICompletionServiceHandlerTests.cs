using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class ModelFeaturesAICompletionServiceHandlerTests
{
    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDeclaresNoMetadata_ShouldNotRemoveTools()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };
        var context = CreateContext(deployment, tools: true);

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.Tools);
        Assert.Single(context.ChatOptions.Tools);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDeclaresToolCalling_ShouldKeepTools()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var context = CreateContext(deployment, tools: true);

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.Tools);
        Assert.Single(context.ChatOptions.Tools);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotDeclareToolCalling_ShouldRemoveTools()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIModelFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: true);
        context.ChatOptions.ToolMode = ChatToolMode.RequireAny;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.Tools);
        Assert.Null(context.ChatOptions.ToolMode);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotDeclareStructuredOutputs_ShouldRemoveJsonResponseFormat()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIModelFeatureNames.ToolCalling);
        var context = CreateContext(deployment, tools: false);
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.ResponseFormat);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDeclaresStructuredOutputs_ShouldKeepJsonResponseFormat()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIModelFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: false);
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.ResponseFormat);
    }

    [Fact]
    public void AddCoreAIModelCapabilities_ShouldRegisterTheTrainedFeatureSet()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCoreAIModelCapabilities();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AIModelCapabilityOptions>>().Value;

        // Assert
        Assert.Contains(AIModelFeatureNames.ToolCalling, options.Features.Keys);
        Assert.Contains(AIModelFeatureNames.ImageInput, options.Features.Keys);
        Assert.Contains(AIModelFeatureNames.ImageOutput, options.Features.Keys);
        Assert.Contains(AIModelFeatureNames.VideoInput, options.Features.Keys);
        Assert.DoesNotContain("webSearch", options.Features.Keys);
        Assert.True(options.Features[AIModelFeatureNames.ToolCalling].EnabledByDefault);
        Assert.True(options.Features[AIModelFeatureNames.Streaming].EnabledByDefault);
        Assert.False(options.Features[AIModelFeatureNames.Reasoning].EnabledByDefault);
        Assert.Equal(AIModelFeatureNames.Reasoning, options.Parameters[AIModelParameterNames.ReasoningEffort].RequiredFeature);
    }

    [Fact]
    public void Clone_ShouldCopyTheRequiredFeature()
    {
        // Arrange
        var descriptor = new AIModelParameterDescriptor
        {
            Name = AIModelParameterNames.ReasoningEffort,
            RequiredFeature = AIModelFeatureNames.Reasoning,
        };

        // Act
        var clone = descriptor.Clone();

        // Assert
        Assert.Equal(AIModelFeatureNames.Reasoning, clone.RequiredFeature);
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

    private static CompletionServiceConfigureContext CreateContext(AIDeployment deployment, bool tools)
    {
        var completionContext = new AICompletionContext
        {
            ChatDeploymentName = deployment.Name,
        };

        var chatOptions = new ChatOptions();

        if (tools)
        {
            chatOptions.Tools = [new TestAIFunction("sample-tool")];
        }

        return new CompletionServiceConfigureContext(chatOptions, completionContext, isFunctionInvocationSupported: true)
        {
            Deployment = deployment,
            DeploymentName = deployment.Name,
        };
    }

    private static ModelFeaturesAICompletionServiceHandler CreateHandler()
    {
        var options = new AIModelCapabilityOptions();
        options.AddFeature(AIModelFeatureNames.ToolCalling, new LocalizedString("Tool calling", "Tool calling"));
        options.AddFeature(AIModelFeatureNames.StructuredOutputs, new LocalizedString("Structured outputs", "Structured outputs"));

        var service = new DefaultAIModelCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());

        return new ModelFeaturesAICompletionServiceHandler(service, NullLogger<ModelFeaturesAICompletionServiceHandler>.Instance);
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
