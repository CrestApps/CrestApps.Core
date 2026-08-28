using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// A single placement a source accepts for a declared parameter — for example "query string parameter"
/// on the built-in HTTP source. Sources advertise their options through
/// <see cref="AIToolInstanceParameterCapabilities"/> so the management UI can offer exactly the
/// placements the source knows how to honor, and so saving an unknown placement fails validation instead
/// of being dropped at invocation time.
/// </summary>
public sealed class AIToolParameterBindingOption
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolParameterBindingOption"/> class.
    /// </summary>
    /// <param name="target">The binding target identifier stored in <see cref="AIToolInstanceParameter.Binding"/>.</param>
    public AIToolParameterBindingOption(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);

        Target = target;
    }

    /// <summary>
    /// Gets the target identifier, such as <c>Query</c>. This is the portion before the colon in a
    /// parameter's <see cref="AIToolInstanceParameter.Binding"/> value.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Gets or sets the friendly name shown in the placement dropdown.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a short hint explaining the placement, shown beneath the dropdown.
    /// </summary>
    public LocalizedString Hint { get; set; }

    /// <summary>
    /// Gets or sets whether the binding accepts a target name distinct from the parameter name (for
    /// example a query key that differs from the parameter's schema name). When <see langword="false"/>,
    /// the binding is stored as the bare target.
    /// </summary>
    public bool SupportsTargetName { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a parameter bound to this placement must always produce a value. A URL path
    /// token is the motivating case: an omitted value would leave a literal token in the request URL, so
    /// a model-filled parameter bound here must be required or carry a default.
    /// </summary>
    public bool RequiresValue { get; set; }

    /// <summary>
    /// Gets or sets the fill modes this placement accepts. When <see langword="null"/> every mode is
    /// allowed. The built-in HTTP source uses this to forbid model-supplied header values, which would
    /// otherwise let a prompt-injected model set arbitrary request headers.
    /// </summary>
    public AIToolParameterFill[] AllowedFills { get; set; }

    /// <summary>
    /// Determines whether this placement accepts the supplied fill mode.
    /// </summary>
    /// <param name="fill">The fill mode to test.</param>
    /// <returns><see langword="true"/> when the fill mode is allowed; otherwise <see langword="false"/>.</returns>
    public bool AllowsFill(AIToolParameterFill fill)
        => AllowedFills is null || Array.IndexOf(AllowedFills, fill) >= 0;
}
