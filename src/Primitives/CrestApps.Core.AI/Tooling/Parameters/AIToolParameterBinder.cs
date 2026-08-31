using CrestApps.Core.AI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Resolves the value of every parameter declared on an <see cref="AIToolInstance"/> for a single
/// invocation, so a source can place the values without re-implementing precedence, type coercion, and
/// validation.
/// </summary>
/// <remarks>
/// This is the half of parameter support the framework can guarantee. Declaring a parameter and
/// resolving its value are universal; placing the value is not, which is why the result is handed back to
/// the source rather than applied here.
/// </remarks>
public static class AIToolParameterBinder
{
    /// <summary>
    /// Reads the parameters declared on an instance.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>The declared parameters, or an empty list when the instance declares none.</returns>
    public static IReadOnlyList<AIToolInstanceParameter> GetParameters(AIToolInstance instance)
    {
        if (instance is null)
        {
            return [];
        }

        return instance.GetOrCreate<AIToolInstanceParametersMetadata>().Parameters ?? [];
    }

    /// <summary>
    /// Resolves every declared parameter for one invocation.
    /// </summary>
    /// <param name="parameters">The declared parameters.</param>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="services">The request services, used to resolve context parameters.</param>
    /// <param name="unprotect">
    /// An optional delegate that unprotects a stored secret value, supplied by the owning source so the
    /// source's own data-protection purpose is used.
    /// </param>
    /// <returns>The resolution result, carrying either the resolved values or the errors to report.</returns>
    public static AIToolParameterResolution Resolve(
        IReadOnlyList<AIToolInstanceParameter> parameters,
        AIFunctionArguments arguments,
        IServiceProvider services,
        Func<string, string> unprotect = null)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return AIToolParameterResolution.Empty;
        }

        var resolved = new List<AIToolResolvedParameter>(parameters.Count);
        List<string> errors = null;

        foreach (var parameter in parameters)
        {
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            if (!TryResolveValue(parameter, arguments, services, unprotect, out var value, out var error))
            {
                if (error is not null)
                {
                    (errors ??= []).Add(error);
                }

                continue;
            }

            AIToolParameterBinding.TryParse(parameter.Binding, parameter.Name, out var binding);

            resolved.Add(new AIToolResolvedParameter(parameter, binding, value));
        }

        return new AIToolParameterResolution(resolved, errors);
    }

    private static bool TryResolveValue(
        AIToolInstanceParameter parameter,
        AIFunctionArguments arguments,
        IServiceProvider services,
        Func<string, string> unprotect,
        out object value,
        out string error)
    {
        value = null;
        error = null;

        object raw;

        switch (parameter.Fill)
        {
            case AIToolParameterFill.Fixed:
                raw = parameter.DefaultValue;

                if (parameter.IsSecret && unprotect is not null && raw is string secret)
                {
                    raw = unprotect(secret);
                }

                if (raw is null)
                {
                    // A pinned parameter with no value was configured but never filled in. Skipping it
                    // silently is the right call: it is the user's own configuration gap, and failing the
                    // call would tell the model nothing it can act on.
                    return false;
                }

                break;

            case AIToolParameterFill.Context:
                if (!TryResolveContext(parameter.ContextKey, services, out raw))
                {
                    error = $"The '{parameter.Name}' parameter could not be resolved from the current context.";

                    return false;
                }

                break;

            default:
                // The model supplies the value; fall back to the configured default when it is omitted.
                if (arguments is null ||
                    !arguments.TryGetFirst(parameter.Name, out raw) ||
                    raw is null)
                {
                    raw = parameter.DefaultValue;
                }

                if (raw is null)
                {
                    if (parameter.Required)
                    {
                        error = $"The required '{parameter.Name}' parameter was not supplied.";
                    }

                    return false;
                }

                break;
        }

        if (!AIToolParameterValueConverter.TryConvert(raw, parameter.Type, out value))
        {
            error = $"The '{parameter.Name}' parameter must be a valid {DescribeType(parameter.Type)}.";

            return false;
        }

        if (!IsAllowed(parameter, value))
        {
            error = $"The '{parameter.Name}' parameter must be one of: {string.Join(", ", parameter.AllowedValues)}.";

            return false;
        }

        return true;
    }

    private static bool TryResolveContext(string contextKey, IServiceProvider services, out object value)
    {
        value = null;

        if (string.IsNullOrEmpty(contextKey))
        {
            return false;
        }

        var resolvers = services?.GetServices<IAIToolParameterContextResolver>();

        if (resolvers is null)
        {
            return false;
        }

        foreach (var resolver in resolvers)
        {
            if (resolver.TryResolve(contextKey, services, out value) && value is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowed(AIToolInstanceParameter parameter, object value)
    {
        if (parameter.AllowedValues is not { Length: > 0 })
        {
            return true;
        }

        var text = AIToolParameterValueConverter.ToStringValue(value);

        foreach (var allowed in parameter.AllowedValues)
        {
            if (string.Equals(allowed?.Trim(), text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeType(AIToolParameterType type)
    {
        return type switch
        {
            AIToolParameterType.Integer => "whole number",
            AIToolParameterType.Number => "number",
            AIToolParameterType.Boolean => "true or false value",
            AIToolParameterType.Array => "array",
            AIToolParameterType.Object => "object",
            _ => "string",
        };
    }
}
