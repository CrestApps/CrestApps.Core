using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Declares that an <see cref="IAIToolInstanceSource"/> knows how to honor user-declared parameters, and
/// describes the placements it accepts. Parameter support is opt-in per source: the framework can always
/// declare a parameter in the schema and resolve its value, but only the source can place that value into
/// the call it makes. A source that does not declare capabilities has nowhere to put a resolved value, so
/// the management UI hides the parameter editor for it and validation rejects any parameters saved
/// against it.
/// </summary>
public sealed class AIToolInstanceParameterCapabilities
{
    /// <summary>
    /// Gets or sets the placements the source accepts. A source must declare at least one placement for
    /// parameter support to be considered enabled.
    /// </summary>
    public IReadOnlyList<AIToolParameterBindingOption> Bindings { get; set; } = [];

    /// <summary>
    /// Gets or sets the argument names the source uses for its own built-in arguments. A declared
    /// parameter may not take one of these names, because it would shadow the source's own argument in
    /// the single schema the model sees.
    /// </summary>
    public string[] ReservedNames { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional description of what parameters do for this particular source, shown above
    /// the parameter editor.
    /// </summary>
    public LocalizedString Hint { get; set; }

    /// <summary>
    /// Gets a value indicating whether this source accepts declared parameters.
    /// </summary>
    public bool Supported => Bindings is { Count: > 0 };

    /// <summary>
    /// Finds the declared placement matching the supplied target identifier.
    /// </summary>
    /// <param name="target">The target identifier, compared case-insensitively.</param>
    /// <returns>The matching option, or <see langword="null"/> when the source does not declare it.</returns>
    public AIToolParameterBindingOption FindBinding(string target)
    {
        if (string.IsNullOrEmpty(target) || Bindings is null)
        {
            return null;
        }

        foreach (var binding in Bindings)
        {
            if (string.Equals(binding.Target, target, StringComparison.OrdinalIgnoreCase))
            {
                return binding;
            }
        }

        return null;
    }
}
