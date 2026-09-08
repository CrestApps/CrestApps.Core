using System.Globalization;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default <see cref="IAIDeploymentParameterApplier"/>, which resolves the effective
/// capabilities of a deployment and binds every selected value the deployment actually exposes.
/// </summary>
public sealed class DefaultAIDeploymentParameterApplier : IAIDeploymentParameterApplier
{
    private readonly IAIDeploymentCapabilityService _capabilityService;
    private readonly IEnumerable<IAIDeploymentParameterBinder> _binders;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDeploymentParameterApplier"/> class.
    /// </summary>
    /// <param name="capabilityService">The capability service used to resolve deployment metadata.</param>
    /// <param name="binders">The registered parameter binders.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIDeploymentParameterApplier(
        IAIDeploymentCapabilityService capabilityService,
        IEnumerable<IAIDeploymentParameterBinder> binders,
        ILogger<DefaultAIDeploymentParameterApplier> logger)
    {
        _capabilityService = capabilityService;
        _binders = binders;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ApplyAsync(
        ChatOptions options,
        AIDeployment deployment,
        AICompletionContext completionContext,
        AIDeploymentParameterScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(completionContext);

        var capabilities = deployment is not null
            ? _capabilityService.GetCapabilities(deployment)
            : await _capabilityService.GetCapabilitiesAsync(GetDeploymentName(completionContext, scope), cancellationToken);

        if (capabilities.Parameters.Count == 0)
        {
            return;
        }

        var values = scope == AIDeploymentParameterScope.Utility
            ? completionContext.UtilityModelParameters
            : completionContext.ModelParameters;

        foreach (var descriptor in capabilities.Parameters)
        {
            var value = GetValue(values, descriptor);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var bindingContext = new AIDeploymentParameterBindingContext(descriptor, value, options, completionContext)
            {
                Deployment = deployment,
            };

            var binder = _binders.FirstOrDefault(candidate => string.Equals(candidate.ParameterName, descriptor.Name, StringComparison.OrdinalIgnoreCase));

            if (binder is null)
            {
                if (TryConvertValue(descriptor, value, out var converted))
                {
                    options.AdditionalProperties ??= [];
                    options.AdditionalProperties[descriptor.Name] = converted;
                }
                else
                {
                    _logger.LogWarning("The value '{Value}' could not be converted for the model parameter '{Parameter}'. The parameter was skipped.", value, descriptor.Name);
                }

                continue;
            }

            await binder.BindAsync(bindingContext, cancellationToken);
        }
    }

    private static string GetDeploymentName(AICompletionContext completionContext, AIDeploymentParameterScope scope)
    {
        if (scope != AIDeploymentParameterScope.Utility)
        {
            return completionContext.ChatDeploymentName;
        }

        return string.IsNullOrWhiteSpace(completionContext.UtilityDeploymentName)
            ? completionContext.ChatDeploymentName
            : completionContext.UtilityDeploymentName;
    }

    private static bool TryConvertValue(AIDeploymentParameterDescriptor descriptor, string value, out object converted)
    {
        // The value has already been validated against the descriptor, so conversion is expected to
        // succeed. Converting to the underlying primitive avoids sending numeric and Boolean values as
        // quoted strings, which some providers reject. When conversion cannot produce the typed value,
        // the parameter is skipped rather than sent as a mismatched string.
        switch (descriptor.Kind)
        {
            case AIDeploymentParameterKind.Integer:
                // Parse through decimal so the Int64 bounds are checked exactly. Using double would
                // round long.MaxValue up to 2^63, allowing an out-of-range value to overflow the cast.
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var integral) &&
                    integral == Math.Truncate(integral) &&
                    integral >= long.MinValue &&
                    integral <= long.MaxValue)
                {
                    converted = (long)integral;

                    return true;
                }

                break;

            case AIDeploymentParameterKind.Number:
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                {
                    converted = number;

                    return true;
                }

                break;

            case AIDeploymentParameterKind.Boolean:
                if (bool.TryParse(value, out var boolean))
                {
                    converted = boolean;

                    return true;
                }

                break;

            default:
                converted = value;

                return true;
        }

        converted = null;

        return false;
    }

    private string GetValue(Dictionary<string, string> values, AIDeploymentParameterDescriptor descriptor)
    {
        if (values is null || !values.TryGetValue(descriptor.Name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return descriptor.DefaultValue;
        }

        if (!descriptor.IsValidValue(value))
        {
            _logger.LogWarning("The value '{Value}' is not valid for the model parameter '{Parameter}'. The deployment default is used instead.", value, descriptor.Name);

            return descriptor.DefaultValue;
        }

        return value;
    }
}
