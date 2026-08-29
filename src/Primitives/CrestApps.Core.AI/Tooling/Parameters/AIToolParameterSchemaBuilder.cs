using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Merges the user-declared parameters of an <see cref="AIToolInstance"/> into the JSON schema a source
/// exposes to the AI model.
/// </summary>
/// <remarks>
/// Only <see cref="AIToolParameterFill.Model"/> parameters are emitted. Fixed and context parameters are
/// resolved entirely server-side and must never appear in the schema — a model that can see a parameter
/// will try to fill it, which would defeat the point of pinning or injecting the value in the first place.
/// </remarks>
public static class AIToolParameterSchemaBuilder
{
    /// <summary>
    /// Builds the function schema by adding the declared parameters to a source's own base schema.
    /// </summary>
    /// <param name="baseSchema">
    /// The schema the source builds for its own arguments. The object is not modified; the returned
    /// element is built from a clone. Pass <see langword="null"/> when the source has no arguments of its
    /// own.
    /// </param>
    /// <param name="parameters">The declared parameters, or <see langword="null"/> when the instance declares none.</param>
    /// <returns>The merged schema.</returns>
    public static JsonElement Merge(JsonObject baseSchema, IEnumerable<AIToolInstanceParameter> parameters)
    {
        var schema = baseSchema is null
            ? CreateEmptySchema()
            : baseSchema.DeepClone().AsObject();

        if (schema["type"] is null)
        {
            schema["type"] = "object";
        }

        if (schema["properties"] is not JsonObject properties)
        {
            properties = [];
            schema["properties"] = properties;
        }

        if (parameters is not null)
        {
            var required = schema["required"] as JsonArray;

            foreach (var parameter in parameters)
            {
                if (parameter is null ||
                    parameter.Fill != AIToolParameterFill.Model ||
                    string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                properties[parameter.Name] = BuildProperty(parameter);

                if (!parameter.Required)
                {
                    continue;
                }

                if (required is null)
                {
                    required = [];
                    schema["required"] = required;
                }

                required.Add(parameter.Name);
            }
        }

        schema["additionalProperties"] = false;

        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// Determines whether the merged schema is eligible for provider strict mode, which requires every
    /// declared property to be required and every object to be closed.
    /// </summary>
    /// <param name="hasSourceArguments">
    /// Whether the source contributes open-ended arguments of its own. A free-form object argument — such
    /// as the HTTP source's request body — is never strict-eligible.
    /// </param>
    /// <param name="parameters">The declared parameters.</param>
    /// <returns><see langword="true"/> when strict mode can be enabled for the function.</returns>
    public static bool IsStrictEligible(bool hasSourceArguments, IEnumerable<AIToolInstanceParameter> parameters)
    {
        if (hasSourceArguments || parameters is null)
        {
            return false;
        }

        var declared = 0;

        foreach (var parameter in parameters)
        {
            if (parameter is null || parameter.Fill != AIToolParameterFill.Model)
            {
                continue;
            }

            // An optional property cannot be expressed under strict mode, and an unconstrained array or
            // object has no closed schema to validate against.
            if (!parameter.Required ||
                parameter.Type is AIToolParameterType.Array or AIToolParameterType.Object)
            {
                return false;
            }

            declared++;
        }

        return declared > 0;
    }

    private static JsonObject BuildProperty(AIToolInstanceParameter parameter)
    {
        var property = new JsonObject
        {
            ["type"] = ToSchemaType(parameter.Type),
        };

        var description = BuildDescription(parameter);

        if (!string.IsNullOrEmpty(description))
        {
            property["description"] = description;
        }

        if (parameter.AllowedValues is { Length: > 0 })
        {
            var values = new JsonArray();

            foreach (var allowed in parameter.AllowedValues)
            {
                if (string.IsNullOrWhiteSpace(allowed))
                {
                    continue;
                }

                values.Add(AIToolParameterValueConverter.TryConvert(allowed.Trim(), parameter.Type, out var converted)
                    ? JsonValue.Create(converted)
                    : JsonValue.Create(allowed.Trim()));
            }

            if (values.Count > 0)
            {
                property["enum"] = values;
            }
        }

        return property;
    }

    private static string BuildDescription(AIToolInstanceParameter parameter)
    {
        var description = parameter.Description?.Trim();

        // The default is applied by the binder rather than the model, but telling the model what happens
        // when it omits the value measurably reduces needless guessing.
        if (parameter.Required || parameter.DefaultValue is null)
        {
            return description;
        }

        var defaultText = AIToolParameterValueConverter.ToStringValue(parameter.DefaultValue);

        if (string.IsNullOrEmpty(defaultText))
        {
            return description;
        }

        var hint = $"Optional. Defaults to {defaultText} when omitted.";

        return string.IsNullOrEmpty(description)
            ? hint
            : $"{description} {hint}";
    }

    private static string ToSchemaType(AIToolParameterType type)
    {
        return type switch
        {
            AIToolParameterType.Integer => "integer",
            AIToolParameterType.Number => "number",
            AIToolParameterType.Boolean => "boolean",
            AIToolParameterType.Array => "array",
            AIToolParameterType.Object => "object",
            _ => "string",
        };
    }

    private static JsonObject CreateEmptySchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
        };
    }
}
