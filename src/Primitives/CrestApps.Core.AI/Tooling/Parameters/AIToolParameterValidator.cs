using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Validates the parameters declared on an <see cref="AIToolInstance"/> against the capabilities of the
/// source that owns it.
/// </summary>
/// <remarks>
/// This is where the opt-in guarantee is enforced. A parameter the owning source cannot place would be
/// declared in the schema, filled by the model, and then dropped — the model would report success on a
/// call that never carried the value. Refusing to save such a configuration is the only way to keep that
/// from happening silently.
/// </remarks>
public static partial class AIToolParameterValidator
{
    /// <summary>
    /// Validates a declared parameter set.
    /// </summary>
    /// <param name="parameters">The declared parameters.</param>
    /// <param name="capabilities">The owning source's capabilities, or <see langword="null"/> when the source does not support parameters.</param>
    /// <returns>The validation errors, each paired with the zero-based index of the offending parameter, or -1 for a set-wide error.</returns>
    public static IReadOnlyList<(int Index, string Error)> Validate(
        IReadOnlyList<AIToolInstanceParameter> parameters,
        AIToolInstanceParameterCapabilities capabilities)
    {
        var errors = new List<(int, string)>();

        if (parameters is null || parameters.Count == 0)
        {
            return errors;
        }

        if (capabilities is null || !capabilities.Supported)
        {
            errors.Add((-1, "The selected source does not support parameters."));

            return errors;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reserved = new HashSet<string>(capabilities.ReservedNames ?? [], StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];

            if (parameter is null)
            {
                continue;
            }

            ValidateName(parameter, index, names, reserved, errors);
            ValidateFill(parameter, index, errors);
            ValidateBinding(parameter, index, capabilities, errors);
        }

        return errors;
    }

    private static void ValidateName(
        AIToolInstanceParameter parameter,
        int index,
        HashSet<string> names,
        HashSet<string> reserved,
        List<(int, string)> errors)
    {
        var name = parameter.Name?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            errors.Add((index, "A parameter name is required."));

            return;
        }

        if (!NamePattern().IsMatch(name))
        {
            errors.Add((index, $"'{name}' is not a valid parameter name. Use letters, digits, and underscores, starting with a letter or underscore."));

            return;
        }

        if (reserved.Contains(name))
        {
            errors.Add((index, $"'{name}' is reserved by this source and cannot be used as a parameter name."));

            return;
        }

        if (!names.Add(name))
        {
            errors.Add((index, $"More than one parameter is named '{name}'. Parameter names must be unique."));
        }
    }

    private static void ValidateFill(AIToolInstanceParameter parameter, int index, List<(int, string)> errors)
    {
        switch (parameter.Fill)
        {
            case AIToolParameterFill.Model when string.IsNullOrWhiteSpace(parameter.Description):
                errors.Add((index, $"'{parameter.Name}' needs a description, because it is the only signal the model has for what to pass."));

                break;

            case AIToolParameterFill.Context when string.IsNullOrWhiteSpace(parameter.ContextKey):
                errors.Add((index, $"'{parameter.Name}' is filled from context, so it needs a context key."));

                break;

            case AIToolParameterFill.Fixed when parameter.DefaultValue is null or "":
                errors.Add((index, $"'{parameter.Name}' is a fixed value, so it needs a value."));

                break;
        }

        if (parameter.Fill != AIToolParameterFill.Fixed && parameter.IsSecret)
        {
            errors.Add((index, $"'{parameter.Name}' can only be marked secret when it is a fixed value."));
        }
    }

    private static void ValidateBinding(
        AIToolInstanceParameter parameter,
        int index,
        AIToolInstanceParameterCapabilities capabilities,
        List<(int, string)> errors)
    {
        if (!AIToolParameterBinding.TryParse(parameter.Binding, parameter.Name, out var binding))
        {
            errors.Add((index, $"'{parameter.Name}' needs a placement so the source knows where to send it."));

            return;
        }

        var option = capabilities.FindBinding(binding.Target);

        if (option is null)
        {
            errors.Add((index, $"'{binding.Target}' is not a placement this source supports."));

            return;
        }

        if (!option.AllowsFill(parameter.Fill))
        {
            errors.Add((index, $"'{parameter.Name}' cannot be placed in {option.DisplayName?.Value ?? binding.Target} with the selected fill mode."));
        }

        if (option.RequiresValue &&
            parameter.Fill == AIToolParameterFill.Model &&
            !parameter.Required &&
            parameter.DefaultValue is null)
        {
            errors.Add((index, $"'{parameter.Name}' fills a {option.DisplayName?.Value ?? binding.Target}, so it must be required or have a default value."));
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
