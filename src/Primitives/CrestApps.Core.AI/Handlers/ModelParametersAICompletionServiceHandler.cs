using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Applies the model parameters selected for the current request to the outgoing
/// <see cref="ChatOptions"/>. Only the parameters exposed by the resolved deployment are applied,
/// which guarantees that unsupported parameters are never sent to a provider.
/// </summary>
public sealed class ModelParametersAICompletionServiceHandler : IAICompletionServiceHandler
{
    private readonly IAIModelCapabilityService _capabilityService;
    private readonly IEnumerable<IAIModelParameterBinder> _binders;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelParametersAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="capabilityService">The capability service used to resolve deployment metadata.</param>
    /// <param name="binders">The registered parameter binders.</param>
    /// <param name="logger">The logger.</param>
    public ModelParametersAICompletionServiceHandler(
        IAIModelCapabilityService capabilityService,
        IEnumerable<IAIModelParameterBinder> binders,
        ILogger<ModelParametersAICompletionServiceHandler> logger)
    {
        _capabilityService = capabilityService;
        _binders = binders;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var capabilities = context.Deployment is not null
            ? _capabilityService.GetCapabilities(context.Deployment)
            : await _capabilityService.GetCapabilitiesAsync(context.CompletionContext.ChatDeploymentName, cancellationToken);

        if (capabilities.Parameters.Count == 0)
        {
            return;
        }

        foreach (var descriptor in capabilities.Parameters)
        {
            var value = GetValue(context.CompletionContext, descriptor);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var bindingContext = new AIModelParameterBindingContext(descriptor, value, context.ChatOptions, context.CompletionContext)
            {
                Deployment = context.Deployment,
            };

            var binder = _binders.FirstOrDefault(candidate => string.Equals(candidate.ParameterName, descriptor.Name, StringComparison.OrdinalIgnoreCase));

            if (binder is null)
            {
                context.ChatOptions.AdditionalProperties ??= [];
                context.ChatOptions.AdditionalProperties[descriptor.Name] = value;

                continue;
            }

            await binder.BindAsync(bindingContext, cancellationToken);
        }
    }

    private string GetValue(AICompletionContext completionContext, AIModelParameterDescriptor descriptor)
    {
        if (!completionContext.ModelParameters.TryGetValue(descriptor.Name, out var value) || string.IsNullOrWhiteSpace(value))
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
