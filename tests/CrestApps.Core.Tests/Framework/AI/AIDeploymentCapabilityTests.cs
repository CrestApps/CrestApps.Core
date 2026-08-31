using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class AIDeploymentCapabilityTests
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
        var deployment = CreateDeployment(new AIDeploymentParameter());

        // Act
        var capabilities = service.GetCapabilities(deployment);
        var descriptor = capabilities.GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(AIDeploymentParameterKind.Choice, descriptor.Kind);
        Assert.Equal(3, descriptor.AllowedValues.Count);
        Assert.Equal("Medium", descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_WhenDeploymentNarrowsAllowedValues_ShouldOnlyExposeSupportedValues()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentParameter
        {
            AllowedValues = ["Low", "High"],
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(["Low", "High"], descriptor.AllowedValues.Select(option => option.Value));
    }

    [Fact]
    public void GetCapabilities_WhenRegisteredDefaultIsNotSupported_ShouldClearTheDefault()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentParameter
        {
            AllowedValues = ["Low"],
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Null(descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_WhenParameterRequiresAFeatureTheDeploymentDoesNotDeclare_ShouldExcludeTheParameter()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = [],
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                [AIDeploymentParameterNames.ReasoningEffort] = new AIDeploymentParameter(),
            },
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.Null(descriptor);
    }

    [Fact]
    public void GetCapabilities_WhenParameterRequiresADeclaredFeature_ShouldExposeTheParameter()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentParameter());

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void GetCapabilities_WhenDeploymentOverridesDefault_ShouldUseTheOverride()
    {
        // Arrange
        var service = CreateService(out _);
        var deployment = CreateDeployment(new AIDeploymentParameter
        {
            DefaultValue = "High",
        });

        // Act
        var descriptor = service.GetCapabilities(deployment).GetParameter(AIDeploymentParameterNames.ReasoningEffort);

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal("High", descriptor.DefaultValue);
    }

    [Fact]
    public void GetCapabilities_ShouldNotMutateTheRegisteredDescriptor()
    {
        // Arrange
        var service = CreateService(out var options);
        var deployment = CreateDeployment(new AIDeploymentParameter
        {
            AllowedValues = ["Low"],
            DefaultValue = "Low",
        });

        // Act
        service.GetCapabilities(deployment);

        // Assert
        var registered = options.Parameters[AIDeploymentParameterNames.ReasoningEffort];
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
        var descriptor = new AIDeploymentParameterDescriptor
        {
            Name = "sampling",
            Kind = AIDeploymentParameterKind.Number,
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
        var descriptor = new AIDeploymentParameterDescriptor
        {
            Name = "seed",
            Kind = AIDeploymentParameterKind.Integer,
        };

        // Act & Assert
        Assert.True(descriptor.IsValidValue("12"));
        Assert.False(descriptor.IsValidValue("12.5"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void IsValidValue_ForNumberParameter_ShouldRejectNonFiniteValues(string value)
    {
        // Arrange
        var descriptor = new AIDeploymentParameterDescriptor
        {
            Name = "sampling",
            Kind = AIDeploymentParameterKind.Number,
        };

        // Act & Assert
        Assert.False(descriptor.IsValidValue(value));
    }

    [Fact]
    public async Task ConfigureAsync_WhenDeploymentExposesReasoningEffort_ShouldApplyTheSelectedValue()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateConfigureContext(CreateDeployment(new AIDeploymentParameter()), ("reasoningEffort", "High"));

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
        var deployment = CreateDeployment(new AIDeploymentParameter
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
    public async Task ConfigureAsync_WhenTheSelectedValueIsNotSupported_ShouldLogWarning()
    {
        // Arrange
        var logger = new Mock<ILogger<ModelParametersAICompletionServiceHandler>>();
        var handler = CreateHandler(logger: logger);
        var deployment = CreateDeployment(new AIDeploymentParameter
        {
            AllowedValues = ["Low", "Medium"],
            DefaultValue = "Low",
        });
        var context = CreateConfigureContext(deployment, ("reasoningEffort", "High"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
#pragma warning disable CA1873
        logger.Verify(
            value => value.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains(AIDeploymentParameterNames.ReasoningEffort, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoValueIsSelected_ShouldApplyTheDeploymentDefault()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateConfigureContext(CreateDeployment(new AIDeploymentParameter
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
            descriptor.Kind = AIDeploymentParameterKind.Choice;
            descriptor.AllowedValues =
            [
                new AIDeploymentParameterOption { Value = "low" },
                new AIDeploymentParameterOption { Value = "high" },
            ];
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["verbosity"] = new AIDeploymentParameter(),
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

    [Theory]
    [InlineData(AIDeploymentParameterKind.Integer, "12", 12L)]
    [InlineData(AIDeploymentParameterKind.Number, "0.5", 0.5d)]
    [InlineData(AIDeploymentParameterKind.Boolean, "true", true)]
    public async Task ConfigureAsync_WhenNoBinderIsRegistered_ShouldWriteTypedPrimitiveToAdditionalProperties(
        AIDeploymentParameterKind kind,
        string storedValue,
        object expected)
    {
        // Arrange
        var options = new AIDeploymentCapabilityOptions();
        options.AddParameter("customParameter", new LocalizedString("Custom", "Custom"), descriptor =>
        {
            descriptor.Kind = kind;
            descriptor.Minimum = 0;
            descriptor.Maximum = 100;
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["customParameter"] = new AIDeploymentParameter(),
            },
        });

        var handler = CreateHandler(options);
        var context = CreateConfigureContext(deployment, ("customParameter", storedValue));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.AdditionalProperties);
        var actual = context.ChatOptions.AdditionalProperties["customParameter"];
        Assert.Equal(expected, actual);
        Assert.IsType(expected.GetType(), actual);
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoBinderIsRegistered_ShouldConvertExponentIntegerToLong()
    {
        // Arrange
        var options = new AIDeploymentCapabilityOptions();
        options.AddParameter("customParameter", new LocalizedString("Custom", "Custom"), descriptor =>
        {
            descriptor.Kind = AIDeploymentParameterKind.Integer;
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["customParameter"] = new AIDeploymentParameter(),
            },
        });

        var handler = CreateHandler(options);
        var context = CreateConfigureContext(deployment, ("customParameter", "1e3"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(context.ChatOptions.AdditionalProperties);
        Assert.Equal(1000L, context.ChatOptions.AdditionalProperties["customParameter"]);
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoBinderIsRegistered_ShouldConvertMaxInt64WithoutOverflow()
    {
        // Arrange
        var options = new AIDeploymentCapabilityOptions();
        options.AddParameter("customParameter", new LocalizedString("Custom", "Custom"), descriptor =>
        {
            descriptor.Kind = AIDeploymentParameterKind.Integer;
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["customParameter"] = new AIDeploymentParameter(),
            },
        });

        var handler = CreateHandler(options);

        // long.MaxValue rounds up to 2^63 as a double, so a double-based cast would overflow. The value
        // just above it must be skipped while the exact maximum converts correctly.
        var context = CreateConfigureContext(deployment, ("customParameter", "9223372036854775807"));
        var overflowContext = CreateConfigureContext(deployment, ("customParameter", "9223372036854775808"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);
        await handler.ConfigureAsync(overflowContext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(long.MaxValue, context.ChatOptions.AdditionalProperties["customParameter"]);
        Assert.Null(overflowContext.ChatOptions.AdditionalProperties);
    }

    [Fact]
    public async Task ConfigureAsync_WhenNoBinderIsRegisteredAndValueCannotConvert_ShouldSkipAndLogWarning()
    {
        // Arrange
        var options = new AIDeploymentCapabilityOptions();
        options.AddParameter("customParameter", new LocalizedString("Custom", "Custom"), descriptor =>
        {
            descriptor.Kind = AIDeploymentParameterKind.Integer;
        });

        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                ["customParameter"] = new AIDeploymentParameter(),
            },
        });

        var logger = new Mock<ILogger<ModelParametersAICompletionServiceHandler>>();
        var handler = CreateHandler(options, logger);

        // A value larger than long.MaxValue passes the descriptor's numeric validation but cannot be
        // represented as a 64-bit integer, so it must be skipped rather than sent as a string.
        var context = CreateConfigureContext(deployment, ("customParameter", "1e30"));

        // Act
        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(context.ChatOptions.AdditionalProperties);
#pragma warning disable CA1873
        logger.Verify(
            value => value.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains("customParameter", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [Fact]
    public void ApplyModelParameters_ShouldCopyTheStoredValuesIntoTheCompletionContext()
    {
        // Arrange
        var context = new AICompletionContext();
        var metadata = new AIDeploymentParametersMetadata
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

    private static AIDeployment CreateDeployment(AIDeploymentParameter parameter)
    {
        var deployment = new AIDeployment
        {
            Name = "gpt-5",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = [AIDeploymentFeatureNames.Reasoning],
            Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
            {
                [AIDeploymentParameterNames.ReasoningEffort] = parameter,
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

    private static AIDeploymentParameterDescriptor CreateReasoningEffortDescriptor()
    {
        return new AIDeploymentParameterDescriptor
        {
            Name = AIDeploymentParameterNames.ReasoningEffort,
            DisplayName = new LocalizedString("Reasoning effort", "Reasoning effort"),
            Kind = AIDeploymentParameterKind.Choice,
            DefaultValue = "Medium",
            AllowedValues =
            [
                new AIDeploymentParameterOption { Value = "Low" },
                new AIDeploymentParameterOption { Value = "Medium" },
                new AIDeploymentParameterOption { Value = "High" },
            ],
        };
    }

    private static AIDeploymentCapabilityOptions CreateOptions()
    {
        var options = new AIDeploymentCapabilityOptions();

        options.AddFeature(AIDeploymentFeatureNames.Reasoning, new LocalizedString("Reasoning", "Reasoning"));

        var reasoningEffort = CreateReasoningEffortDescriptor();

        options.AddParameter(reasoningEffort.Name, reasoningEffort.DisplayName, descriptor =>
        {
            descriptor.Kind = reasoningEffort.Kind;
            descriptor.DefaultValue = reasoningEffort.DefaultValue;
            descriptor.RequiredFeature = AIDeploymentFeatureNames.Reasoning;
            descriptor.AllowedValues = reasoningEffort.AllowedValues;
        });

        return options;
    }

    private static DefaultAIDeploymentCapabilityService CreateService(out AIDeploymentCapabilityOptions options)
    {
        options = CreateOptions();

        return new DefaultAIDeploymentCapabilityService(Options.Create(options), Mock.Of<IAIDeploymentStore>());
    }

    private static ModelParametersAICompletionServiceHandler CreateHandler(
        AIDeploymentCapabilityOptions options = null,
        Mock<ILogger<ModelParametersAICompletionServiceHandler>> logger = null)
    {
        var service = new DefaultAIDeploymentCapabilityService(Options.Create(options ?? CreateOptions()), Mock.Of<IAIDeploymentStore>());

        return new ModelParametersAICompletionServiceHandler(
            service,
            [new ReasoningEffortModelParameterBinder()],
            logger?.Object ?? NullLogger<ModelParametersAICompletionServiceHandler>.Instance);
    }
}
