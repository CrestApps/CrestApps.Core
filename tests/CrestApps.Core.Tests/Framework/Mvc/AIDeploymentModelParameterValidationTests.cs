using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIDeploymentModelParameterValidationTests
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

    private static AIModelParameterDescriptor IntegerDescriptor(double? min = null, double? max = null)
    {
        return new AIModelParameterDescriptor
        {
            Name = "topK",
            DisplayName = new LocalizedString("topK", "Top K"),
            Kind = AIModelParameterKind.Integer,
            Minimum = min,
            Maximum = max,
        };
    }

    [Fact]
    public void ValidateModelParameters_WhenChoiceDefaultIsRegistered_AddsNoError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "reasoningEffort", IsSupported = true, DefaultValue = "Medium" },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.True(modelState.IsValid);
    }

    [Fact]
    public void ValidateModelParameters_WhenChoiceDefaultNotInAllowedSet_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "reasoningEffort", IsSupported = true, DefaultValue = "Ultra" },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].DefaultValue", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenDefaultIsNarrowedOut_AddsError()
    {
        // The deployment narrows the supported values to Low/Medium, so a High default is no longer valid.
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = true,
                    SelectedAllowedValues = ["Low", "Medium"],
                    DefaultValue = "High",
                },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].DefaultValue", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenSelectedValueIsNotRegistered_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel
                {
                    Name = "reasoningEffort",
                    IsSupported = true,
                    SelectedAllowedValues = ["Low", "Bogus"],
                },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].SelectedAllowedValues", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenMinimumGreaterThanMaximum_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "temperature", IsSupported = true, Minimum = 2, Maximum = 1 },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([NumberDescriptor()], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].Minimum", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenDefaultOutsideRange_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "temperature", IsSupported = true, DefaultValue = "5" },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([NumberDescriptor(min: 0, max: 2)], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].DefaultValue", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenIntegerDefaultIsNotWhole_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "topK", IsSupported = true, DefaultValue = "3.5" },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([IntegerDescriptor(min: 0, max: 100)], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].DefaultValue", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenParameterIsNotRegistered_AddsError()
    {
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "unknownParameter", IsSupported = true },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.False(modelState.IsValid);
        Assert.Contains("ModelParameters[0].Name", modelState.Keys);
    }

    [Fact]
    public void ValidateModelParameters_WhenRowIsNotSupported_IgnoresInvalidValues()
    {
        // A feature-dependent row that is not supported posts IsSupported=false and must be skipped so
        // toggling a feature off never blocks the save with validation errors for a hidden row.
        var model = new AIDeploymentViewModel
        {
            ModelParameters =
            [
                new AIDeploymentModelParameterViewModel { Name = "reasoningEffort", IsSupported = false, DefaultValue = "Ultra" },
            ],
        };

        var modelState = new ModelStateDictionary();

        model.ValidateModelParameters([ChoiceDescriptor("Low", "Medium", "High")], modelState);

        Assert.True(modelState.IsValid);
    }
}
