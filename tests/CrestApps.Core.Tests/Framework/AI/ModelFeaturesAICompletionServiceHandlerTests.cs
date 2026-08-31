using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling);
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
        var deployment = CreateDeployment(AIDeploymentFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: true);
        context.ChatOptions.ToolMode = ChatToolMode.RequireAny;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.Tools);
        Assert.Null(context.ChatOptions.ToolMode);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotDeclareToolCalling_ShouldLogWarning()
    {
        // Arrange
        var logger = new Mock<ILogger<ModelFeaturesAICompletionServiceHandler>>();
        var handler = CreateHandler(logger);
        var deployment = CreateDeployment(AIDeploymentFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: true);

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        VerifyWarningLogged(logger, AIDeploymentFeatureNames.ToolCalling);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotDeclareToolCalling_ShouldClearToolModeEvenWithoutTools()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIDeploymentFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: false);
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
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling);
        var context = CreateContext(deployment, tools: false);
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.ResponseFormat);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotDeclareStructuredOutputs_ShouldLogWarning()
    {
        // Arrange
        var logger = new Mock<ILogger<ModelFeaturesAICompletionServiceHandler>>();
        var handler = CreateHandler(logger);
        var deployment = CreateDeployment(AIDeploymentFeatureNames.ToolCalling);
        var context = CreateContext(deployment, tools: false);
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        VerifyWarningLogged(logger, AIDeploymentFeatureNames.StructuredOutputs);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDeclaresStructuredOutputs_ShouldKeepJsonResponseFormat()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(AIDeploymentFeatureNames.StructuredOutputs);
        var context = CreateContext(deployment, tools: false);
        context.ChatOptions.ResponseFormat = ChatResponseFormat.Json;

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.ResponseFormat);
    }

    [Fact]
    public void AddCoreAIDeploymentCapabilities_ShouldRegisterTheTrainedFeatureSet()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCoreAIDeploymentCapabilities();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AIDeploymentCapabilityOptions>>().Value;

        // Assert
        Assert.Contains(AIDeploymentFeatureNames.ToolCalling, options.Features.Keys);
        Assert.Contains(AIDeploymentFeatureNames.ImageInput, options.Features.Keys);
        Assert.Contains(AIDeploymentFeatureNames.ImageOutput, options.Features.Keys);
        Assert.Contains(AIDeploymentFeatureNames.VideoInput, options.Features.Keys);
        Assert.Contains(AIDeploymentFeatureNames.VideoOutput, options.Features.Keys);
        Assert.DoesNotContain("webSearch", options.Features.Keys);
        Assert.DoesNotContain("computerUse", options.Features.Keys);
        Assert.True(options.Features[AIDeploymentFeatureNames.ToolCalling].EnabledByDefault);
        Assert.True(options.Features[AIDeploymentFeatureNames.Streaming].EnabledByDefault);
        Assert.False(options.Features[AIDeploymentFeatureNames.Reasoning].EnabledByDefault);
        Assert.Equal(AIDeploymentFeatureNames.Reasoning, options.Parameters[AIDeploymentParameterNames.ReasoningEffort].RequiredFeature);
    }

    [Fact]
    public void Clone_ShouldCopyTheRequiredFeature()
    {
        // Arrange
        var descriptor = new AIDeploymentParameterDescriptor
        {
            Name = AIDeploymentParameterNames.ReasoningEffort,
            RequiredFeature = AIDeploymentFeatureNames.Reasoning,
        };

        // Act
        var clone = descriptor.Clone();

        // Assert
        Assert.Equal(AIDeploymentFeatureNames.Reasoning, clone.RequiredFeature);
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

    private static ModelFeaturesAICompletionServiceHandler CreateHandler(Mock<ILogger<ModelFeaturesAICompletionServiceHandler>> logger = null)
    {
        var options = new AIDeploymentCapabilityOptions();
        options.AddFeature(AIDeploymentFeatureNames.ToolCalling, new LocalizedString("Tool calling", "Tool calling"));
        options.AddFeature(AIDeploymentFeatureNames.StructuredOutputs, new LocalizedString("Structured outputs", "Structured outputs"));

        var service = new DefaultAIDeploymentCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());

        return new ModelFeaturesAICompletionServiceHandler(service, logger?.Object ?? NullLogger<ModelFeaturesAICompletionServiceHandler>.Instance);
    }

    private static void VerifyWarningLogged(Mock<ILogger<ModelFeaturesAICompletionServiceHandler>> logger, string feature)
    {
#pragma warning disable CA1873
        logger.Verify(
            value => value.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains(feature, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
#pragma warning restore CA1873
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
