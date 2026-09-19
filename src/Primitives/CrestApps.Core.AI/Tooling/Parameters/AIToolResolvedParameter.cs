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
