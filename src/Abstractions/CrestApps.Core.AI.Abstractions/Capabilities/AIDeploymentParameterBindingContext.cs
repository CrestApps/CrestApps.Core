using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Carries the information required by an <see cref="IAIDeploymentParameterBinder"/> to apply a selected
/// model parameter value to the outgoing request.
/// </summary>
public sealed class AIDeploymentParameterBindingContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentParameterBindingContext"/> class.
    /// </summary>
    /// <param name="descriptor">The effective descriptor of the parameter being applied.</param>
    /// <param name="value">The value selected by the operator.</param>
    /// <param name="chatOptions">The chat options to mutate.</param>
    /// <param name="completionContext">The completion context of the current request.</param>
    public AIDeploymentParameterBindingContext(
        AIDeploymentParameterDescriptor descriptor,
        string value,
        ChatOptions chatOptions,
        AICompletionContext completionContext)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(chatOptions);
        ArgumentNullException.ThrowIfNull(completionContext);

        Descriptor = descriptor;
        Value = value;
        ChatOptions = chatOptions;
        CompletionContext = completionContext;
    }

    /// <summary>
    /// Gets the effective descriptor of the parameter being applied.
    /// </summary>
    public AIDeploymentParameterDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the value selected by the operator.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the chat options to mutate.
    /// </summary>
    public ChatOptions ChatOptions { get; }

    /// <summary>
    /// Gets the completion context of the current request.
    /// </summary>
    public AICompletionContext CompletionContext { get; }

    /// <summary>
    /// Gets or sets the deployment resolved for the current request.
    /// </summary>
    public AIDeployment Deployment { get; set; }
}
