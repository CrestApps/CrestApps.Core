using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class DefaultAIOptionsTests
{
    [Fact]
    public void New_WhenNothingIsConfigured_LeavesSamplingParametersUnset()
    {
        var options = new DefaultAIOptions();

        Assert.Null(options.Temperature);
        Assert.Null(options.MaxOutputTokens);
        Assert.Null(options.TopP);
        Assert.Null(options.FrequencyPenalty);
        Assert.Null(options.PresencePenalty);
    }

    [Fact]
    public void New_WhenNothingIsConfigured_AllowsThirtyIterationsPerRequest()
    {
        var options = new DefaultAIOptions();

        Assert.Equal(30, options.MaximumIterationsPerRequest);
        Assert.Equal(100, options.AbsoluteMaximumIterationsPerRequest);
    }

    [Fact]
    public void ApplySiteOverrides_WhenSamplingParametersAreUnset_KeepsThemUnset()
    {
        var result = new DefaultAIOptions().ApplySiteOverrides(new GeneralAIOptions());

        Assert.Null(result.TopP);
        Assert.Null(result.FrequencyPenalty);
        Assert.Null(result.PresencePenalty);
        Assert.Equal(30, result.MaximumIterationsPerRequest);
    }

    [Fact]
    public void Normalize_WhenAbsoluteMaximumIsNotPositive_ResetsToDefaultAndClampsMaximum()
    {
        var options = new DefaultAIOptions
        {
            MaximumIterationsPerRequest = 150,
            AbsoluteMaximumIterationsPerRequest = 0,
        };

        options.Normalize();

        Assert.Equal(100, options.AbsoluteMaximumIterationsPerRequest);
        Assert.Equal(100, options.MaximumIterationsPerRequest);
    }

    [Fact]
    public void Normalize_WhenMaximumIterationsIsNotPositive_ResetsToDefault()
    {
        var options = new DefaultAIOptions
        {
            MaximumIterationsPerRequest = 0,
            AbsoluteMaximumIterationsPerRequest = 50,
        };

        options.Normalize();

        Assert.Equal(30, options.MaximumIterationsPerRequest);
        Assert.Equal(50, options.AbsoluteMaximumIterationsPerRequest);
    }

    [Fact]
    public void ApplySiteOverrides_WhenOverrideValuesExceedAbsoluteMaximum_ClampsResult()
    {
        var options = new DefaultAIOptions
        {
            MaximumIterationsPerRequest = 8,
            AbsoluteMaximumIterationsPerRequest = 12,
            EnableDistributedCaching = true,
            EnableOpenTelemetry = false,
        };

        var result = options.ApplySiteOverrides(new GeneralAIOptions
        {
            OverrideMaximumIterationsPerRequest = true,
            MaximumIterationsPerRequest = 50,
            OverrideEnableDistributedCaching = true,
            EnableDistributedCaching = false,
            OverrideEnableOpenTelemetry = true,
            EnableOpenTelemetry = true,
        });

        Assert.Equal(12, result.MaximumIterationsPerRequest);
        Assert.Equal(12, result.AbsoluteMaximumIterationsPerRequest);
        Assert.False(result.EnableDistributedCaching);
        Assert.True(result.EnableOpenTelemetry);
    }

    [Fact]
    public void ApplySiteOverrides_WhenOverridesAreDisabled_PreservesAppSettingsValues()
    {
        var options = new DefaultAIOptions
        {
            MaximumIterationsPerRequest = 7,
            AbsoluteMaximumIterationsPerRequest = 20,
            EnableDistributedCaching = false,
            EnableOpenTelemetry = true,
        };

        var result = options.ApplySiteOverrides(new GeneralAIOptions
        {
            OverrideMaximumIterationsPerRequest = false,
            MaximumIterationsPerRequest = 15,
            OverrideEnableDistributedCaching = false,
            EnableDistributedCaching = true,
            OverrideEnableOpenTelemetry = false,
            EnableOpenTelemetry = false,
        });

        Assert.Equal(7, result.MaximumIterationsPerRequest);
        Assert.Equal(20, result.AbsoluteMaximumIterationsPerRequest);
        Assert.False(result.EnableDistributedCaching);
        Assert.True(result.EnableOpenTelemetry);
    }
}
