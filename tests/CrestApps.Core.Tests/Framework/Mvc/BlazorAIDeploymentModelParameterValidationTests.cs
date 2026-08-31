using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class BlazorAIDeploymentModelParameterValidationTests
{
    private static AIModelParameterDescriptor ChoiceDescriptor(params string[] values)
    {
        return new AIModelParameterDescriptor
        {
            Name = "reasoningEffort",
            DisplayName = new LocalizedString("reasoningEffort", "Reasoning effort"),
            Kind = AIModelParameterKind.Choice,
            AllowedValues = [.. values.Select(value => new AIModelParameterOption { Value = value })],
        };
    }

    private static AIModelParameterDescriptor NumberDescriptor(double? min = null, double? max = null)
    {
        return new AIModelParameterDescriptor
        {
            Name = "temperature",
            DisplayName = new LocalizedString("temperature", "Temperature"),
            Kind = AIModelParameterKind.Number,
            Minimum = min,
            Maximum = max,
        };
    }

    [Fact]
    public void ValidateModelParameters_WhenChoiceDefaultIsRegistered_ReturnsNoErrors()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = true,
                    DefaultValue = "Medium",
                    Descriptor = ChoiceDescriptor("Low", "Medium", "High"),
                },
            ],
        };

        Assert.Empty(model.ValidateModelParameters());
    }

    [Fact]
    public void ValidateModelParameters_WhenChoiceDefaultNotInAllowedSet_ReturnsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = true,
                    DefaultValue = "Ultra",
                    Descriptor = ChoiceDescriptor("Low", "Medium", "High"),
                },
            ],
        };

        Assert.NotEmpty(model.ValidateModelParameters());
    }

    [Fact]
    public void ValidateModelParameters_WhenDefaultIsNarrowedOut_ReturnsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = true,
                    SelectedAllowedValues = new HashSet<string>(["Low", "Medium"], StringComparer.OrdinalIgnoreCase),
                    DefaultValue = "High",
                    Descriptor = ChoiceDescriptor("Low", "Medium", "High"),
                },
            ],
        };

        Assert.NotEmpty(model.ValidateModelParameters());
    }

    [Fact]
    public void ValidateModelParameters_WhenMinimumGreaterThanMaximum_ReturnsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "temperature",
                    IsSupported = true,
                    Minimum = 2,
                    Maximum = 1,
                    Descriptor = NumberDescriptor(),
                },
            ],
        };

        Assert.NotEmpty(model.ValidateModelParameters());
    }

    [Fact]
    public void ValidateModelParameters_WhenDefaultOutsideRange_ReturnsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "temperature",
                    IsSupported = true,
                    DefaultValue = "5",
                    Descriptor = NumberDescriptor(min: 0, max: 2),
                },
            ],
        };

        Assert.NotEmpty(model.ValidateModelParameters());
    }

    [Fact]
    public void ValidateModelParameters_WhenRowIsNotSupported_ReturnsNoErrors()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = false,
                    DefaultValue = "Ultra",
                    Descriptor = ChoiceDescriptor("Low", "Medium", "High"),
                },
            ],
        };

        Assert.Empty(model.ValidateModelParameters());
    }
}
