namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// A parsed <see cref="AIToolInstanceParameter.Binding"/> value. Bindings are stored as a single string
/// (<c>Target</c> or <c>Target:name</c>) so they round-trip cleanly through the properties bag and the
/// management UI, and are parsed into this pair when a source places a resolved value.
/// </summary>
/// <param name="Target">The placement identifier, for example <c>Query</c>.</param>
/// <param name="Name">
/// The target name within that placement — the query key, header name, or body path. Falls back to the
/// parameter's own name when the stored binding omits it.
/// </param>
public readonly record struct AIToolParameterBinding(string Target, string Name)
{
    /// <summary>
    /// Parses a stored binding value, defaulting the target name to the supplied parameter name.
    /// </summary>
    /// <param name="binding">The stored binding value, such as <c>Query:orderId</c>.</param>
    /// <param name="parameterName">The parameter name used when the binding carries no explicit target name.</param>
    /// <param name="result">The parsed binding.</param>
    /// <returns><see langword="true"/> when a target could be parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string binding, string parameterName, out AIToolParameterBinding result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(binding))
        {
            return false;
        }

        var separator = binding.IndexOf(':');

        if (separator < 0)
        {
            var bare = binding.Trim();

            if (bare.Length == 0)
            {
                return false;
            }

            result = new AIToolParameterBinding(bare, parameterName);

            return true;
        }

        var target = binding[..separator].Trim();

        if (target.Length == 0)
        {
            return false;
        }

        var name = binding[(separator + 1)..].Trim();

        result = new AIToolParameterBinding(target, name.Length == 0 ? parameterName : name);

        return true;
    }

    /// <summary>
    /// Determines whether this binding targets the supplied placement.
    /// </summary>
    /// <param name="target">The placement identifier to compare, case-insensitively.</param>
    /// <returns><see langword="true"/> when the targets match; otherwise <see langword="false"/>.</returns>
    public bool Is(string target)
        => string.Equals(Target, target, StringComparison.OrdinalIgnoreCase);
}
