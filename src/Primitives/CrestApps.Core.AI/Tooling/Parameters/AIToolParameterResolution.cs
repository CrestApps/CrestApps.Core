namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// A single parameter whose value has been resolved for one invocation, paired with the placement the
/// owning source should apply it to.
/// </summary>
/// <param name="Parameter">The declared parameter.</param>
/// <param name="Binding">The parsed placement, or <see langword="default"/> when the parameter declares none.</param>
/// <param name="Value">The resolved, type-coerced value.</param>
public readonly record struct AIToolResolvedParameter(
    AIToolInstanceParameter Parameter,
    AIToolParameterBinding Binding,
    object Value)
{
    /// <summary>
    /// Gets the resolved value rendered as a string, for placements that carry text such as a query
    /// string value or a URL path segment.
    /// </summary>
    public string StringValue => AIToolParameterValueConverter.ToStringValue(Value);
}

/// <summary>
/// The outcome of resolving every declared parameter for one invocation.
/// </summary>
public sealed class AIToolParameterResolution
{
    /// <summary>
    /// An empty resolution, used by instances that declare no parameters.
    /// </summary>
    public static readonly AIToolParameterResolution Empty = new([], []);

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolParameterResolution"/> class.
    /// </summary>
    /// <param name="parameters">The resolved parameters, in declaration order.</param>
    /// <param name="errors">The errors that prevented one or more parameters from resolving.</param>
    public AIToolParameterResolution(IReadOnlyList<AIToolResolvedParameter> parameters, IReadOnlyList<string> errors)
    {
        Parameters = parameters ?? [];
        Errors = errors ?? [];
    }

    /// <summary>
    /// Gets the successfully resolved parameters, in the order they were declared.
    /// </summary>
    public IReadOnlyList<AIToolResolvedParameter> Parameters { get; }

    /// <summary>
    /// Gets the human-readable errors describing why a parameter could not be resolved. These are
    /// returned to the AI model as a tool result so it can correct the call and retry, rather than
    /// surfacing as an exception.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether every declared parameter resolved.
    /// </summary>
    public bool Succeeded => Errors.Count == 0;

    /// <summary>
    /// Gets the resolved parameters that target the supplied placement.
    /// </summary>
    /// <param name="target">The placement identifier, compared case-insensitively.</param>
    /// <returns>The matching resolved parameters.</returns>
    public IEnumerable<AIToolResolvedParameter> ForTarget(string target)
    {
        foreach (var resolved in Parameters)
        {
            if (resolved.Binding.Is(target))
            {
                yield return resolved;
            }
        }
    }
}
