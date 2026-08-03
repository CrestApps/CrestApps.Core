using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class AIModelCapabilityTests
{
    [Fact]
    public void GetCapabilities_WhenDeploymentDeclaresNoMetadata_ShouldReturnEmpty()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        // Act
        var capabilities = service.GetCapabilities(deployment);

        // Assert
        Assert.Empty(capabilities.Features);
        Assert.Empty(capabilities.Parameters);
    }

    [Fact]
    public void GetCapabilities_WhenDeploymentDeclaresParameter_ShouldReturnRegisteredMetadata()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentModelParameter());

        // Act
        var capabilities = service.GetCapabilities(deployment);
        var descriptor = capabilities.GetParameter(AIModelParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(AIModelParameterKind.Choice, descriptor.Kind);
        Assert.Equal(3, descriptor.AllowedValues.Count);
        Assert.Equal("Medium", descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_WhenDeploymentNarrowsAllowedValues_ShouldOnlyExposeSupportedValues()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentModelParameter
        {
            AllowedValues = ["Low", "High"],
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIModelParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(["Low", "High"], descriptor.AllowedValues.Select(option => option.Value));
    }

    [Fact]
    public void GetCapabilities_WhenRegisteredDefaultIsNotSupported_ShouldClearTheDefault()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentModelParameter
        {
            AllowedValues = ["Low"],
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIModelParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Null(descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_WhenDeploymentOverridesDefault_ShouldUseTheOverride()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentModelParameter
        {
            DefaultValue = "High",
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIModelParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal("High", descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_ShouldNotMutateTheRegisteredDescriptor()
    {
        // Arrange
        var service = CreateService(out var options);
        var deployment = CreateDeployment(new AIDeploymentModelParameter
        {
            AllowedValues = ["Low"],
            DefaultValue = "Low",
        });

        // Act
        service.GetCapabilities(deployment);

        // Assert
        var registered = options.Parameters[AIModelParameterNames.ReasoningEffort];
        Assert.Equal(3, registered.AllowedValues.Count);
        Assert.Equal("Medium", registered.DefaultValue);
    }

    [Theory]
    [InlineData("Low", true)]
    [InlineData("low", true)]
    [InlineData("Insane", false)]
    [InlineData("", true)]
    public void IsValidValue_ForChoiceParameter_ShouldValidateAgainstAllowedValues(string value, bool expected)
    {
        // Arrange
        var descriptor = CreateReasoningEffortDescriptor();

        // Act
        var result = descriptor.IsValidValue(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("2.5", false)]
    [InlineData("not-a-number", false)]
    public void IsValidValue_ForNumberParameter_ShouldHonorRange(string value, bool expected)
    {
        // Arrange
        var descriptor = new AIModelParameterDescriptor
        {
            Name = "sampling",
            Kind = AIModelParameterKind.Number,
            Minimum = 0,
            Maximum = 2,
        };

        // Act
        var result = descriptor.IsValidValue(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidValue_ForIntegerParameter_ShouldRejectFractionalValues()
    {
        // Arrange
        var descriptor = new AIModelParameterDescriptor
        {
            Name = "seed",
            Kind = AIModelParameterKind.Integer,
        };

        // Act & Assert
        Assert.True(descriptor.IsValidValue("12"));
        Assert.False(descriptor.IsValidValue("12.5"));
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentExposesReasoningEffort_ShouldApplyTheSelectedValue()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateConfigureContext(CreateDeployment(new AIDeploymentModelParameter()), ("reasoningEffort", "High"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReasoningEffort.High, context.ChatOptions.Reasoning?.Effort);
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentDoesNotExposeTheParameter_ShouldNotSendIt()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };
        var context = CreateConfigureContext(deployment, ("reasoningEffort", "High"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.Reasoning);
        Assert.Null(context.ChatOptions.AdditionalProperties);
    }

    [Fact]
    public async Task ConfigureAsync_WhenTheSelectedValueIsNotSupported_ShouldFallBackToTheDeploymentDefault()
    {
        // Arrange
        var handler = CreateHandler();
        var deployment = CreateDeployment(new AIDeploymentModelParameter
        {
            AllowedValues = ["Low", "Medium"],
            DefaultValue = "Low",
        });
        var context = CreateConfigureContext(deployment, ("reasoningEffort", "High"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReasoningEffort.Low, context.ChatOptions.Reasoning?.Effort);
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoValueIsSelected_ShouldApplyTheDeploymentDefault()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateConfigureContext(CreateDeployment(new AIDeploymentModelParameter
        {
            DefaultValue = "Low",
        }));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReasoningEffort.Low, context.ChatOptions.Reasoning?.Effort);
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoBinderIsRegistered_ShouldWriteToAdditionalProperties()
    {
        // Arrange
        var options = CreateOptions();
        options.AddParameter("verbosity", new LocalizedString("Verbosity", "Verbosity"), descriptor =>
        {
            descriptor.Kind = AIModelParameterKind.Choice;
            descriptor.AllowedValues =
            [
                new AIModelParameterOption { Value = "low" },
                new AIModelParameterOption { Value = "high" },
            ];
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentModelMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentModelParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["verbosity"] = new AIDeploymentModelParameter(),
            },
        });

        var handler = CreateHandler(options);
        var context = CreateConfigureContext(deployment, ("verbosity", "high"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.AdditionalProperties);
        Assert.Equal("high", context.ChatOptions.AdditionalProperties["verbosity"]);
    }

    [Fact]
    public void ApplyModelParameters_ShouldCopyTheStoredValuesIntoTheCompletionContext()
    {
        // Arrange
        var context = new AICompletionContext();
        var metadata = new AIModelParametersMetadata
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["reasoningEffort"] = "High",
                ["ignored"] = " ",
            },
        };

        // Act
        context.ApplyModelParameters(metadata);

        // Assert
        Assert.Equal("High", context.ModelParameters["reasoningEffort"]);
        Assert.False(context.ModelParameters.ContainsKey("ignored"));
    }

    private static AIDeployment CreateDeployment(AIDeploymentModelParameter parameter)
    {
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentModelMetadata
        {
            Features = [AIModelFeatureNames.Reasoning],
            Parameters = new Dictionary<string, AIDeploymentModelParameter>(StringComparer.OrdinalIgnoreCase)
            {
                [AIModelParameterNames.ReasoningEffort] = parameter,
            },
        });

        return deployment;
    }

    private static CompletionServiceConfigureContext CreateConfigureContext(AIDeployment deployment, params (string Name, string Value)[] values)
    {
        var completionContext = new AICompletionContext
        {
            ChatDeploymentName = deployment.Name,
        };

        foreach (var (name, value) in values)
        {
            completionContext.ModelParameters[name] = value;
        }

        return new CompletionServiceConfigureContext(new ChatOptions(), completionContext, isFunctionInvocationSupported: true)
        {
            Deployment = deployment,
            DeploymentName = deployment.Name,
        };
    }

    private static AIModelParameterDescriptor CreateReasoningEffortDescriptor()
    {
        return new AIModelParameterDescriptor
        {
            Name = AIModelParameterNames.ReasoningEffort,
            DisplayName = new LocalizedString("Reasoning effort", "Reasoning effort"),
            Kind = AIModelParameterKind.Choice,
            DefaultValue = "Medium",
            AllowedValues =
            [
                new AIModelParameterOption { Value = "Low" },
                new AIModelParameterOption { Value = "Medium" },
                new AIModelParameterOption { Value = "High" },
            ],
        };
    }

    private static AIModelCapabilityOptions CreateOptions()
    {
        var options = new AIModelCapabilityOptions();

        options.AddFeature(AIModelFeatureNames.Reasoning, new LocalizedString("Reasoning", "Reasoning"));

        var reasoningEffort = CreateReasoningEffortDescriptor();

        options.AddParameter(reasoningEffort.Name, reasoningEffort.DisplayName, descriptor =>
        {
            descriptor.Kind = reasoningEffort.Kind;
            descriptor.DefaultValue = reasoningEffort.DefaultValue;
            descriptor.AllowedValues = reasoningEffort.AllowedValues;
        });

        return options;
    }

    private static DefaultAIModelCapabilityService CreateService(out AIModelCapabilityOptions options)
    {
        options = CreateOptions();

        return new DefaultAIModelCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());
    }

    private static ModelParametersAICompletionServiceHandler CreateHandler(AIModelCapabilityOptions options = null)
    {
        var service = new DefaultAIModelCapabilityService(Options.Create(options ?? CreateOptions()), Mock.Of<IAIDeploymentStore>());

        return new ModelParametersAICompletionServiceHandler(
            service,
            [new ReasoningEffortModelParameterBinder()],
            NullLogger<ModelParametersAICompletionServiceHandler>.Instance);
    }
}
