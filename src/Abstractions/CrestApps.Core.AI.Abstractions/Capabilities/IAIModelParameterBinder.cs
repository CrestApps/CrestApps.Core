namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Applies the value selected for a registered model parameter to the outgoing chat request.
/// Modules register a binder for every parameter they contribute so runtime behavior stays
/// provider-agnostic and free of model name detection.
/// </summary>
public interface IAIModelParameterBinder
{
    /// <summary>
    /// Gets the technical name of the parameter this binder applies.
    /// </summary>
    string ParameterName { get; }

    /// <summary>
    /// Applies the selected value to the request represented by the given context.
    /// </summary>
    /// <param name="context">The binding context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task BindAsync(AIModelParameterBindingContext context, CancellationToken cancellationToken = default);
}
