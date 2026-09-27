using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Reads a PDF tool's arguments whatever shape they arrive in — JSON elements, CLR values, or numbers and
/// booleans a model wrote as strings.
/// </summary>
internal sealed class PdfToolArguments
{
    private readonly AIFunctionArguments _arguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfToolArguments"/> class.
    /// </summary>
    /// <param name="arguments">The raw arguments.</param>
    public PdfToolArguments(AIFunctionArguments arguments)
    {
        _arguments = arguments;
    }

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services => _arguments.Services;

    /// <summary>
    /// Reads the PDF the call names, from its <c>pdf</c> argument.
    /// </summary>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public string Pdf()
    {
        return GetString("pdf");
    }

    /// <summary>
    /// Reads a string argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or blank.</returns>
    public string GetString(string name)
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Reads a boolean argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public bool? GetBoolean(string name)
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetDouble(out var number) ? number != 0 : null,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed)
                ? parsed
                : element.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "yes" or "1" or "on" => true,
                    "no" or "0" or "off" => false,
                    _ => null,
                },
            _ => null,
        };
    }

    /// <summary>
    /// Reads a number argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or not a number.</returns>
    public double? GetDouble(string name)
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a whole-number argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public int? GetInt(string name)
    {
        var value = GetDouble(name);

        return value.HasValue && value.Value is >= int.MinValue and <= int.MaxValue
            ? (int)Math.Round(value.Value)
            : null;
    }

    /// <summary>
    /// Reads a list of strings, accepting an array or a single string.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="splitCommas">
    /// Whether a single string is split at commas, for lists of names or kinds; leave it off for values that
    /// may contain a comma themselves, such as text to find.
    /// </param>
    /// <returns>The values, or an empty list.</returns>
    public List<string> GetStrings(string name, bool splitCommas = false)
    {
        var values = new List<string>();

        if (!TryGetElement(name, out var element))
        {
            return values;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text.Trim());
                }
            }

            return values;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();

            if (splitCommas)
            {
                values.AddRange(text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text.Trim());
            }
        }

        return values;
    }

    /// <summary>
    /// Reads a page selection, accepting a string such as <c>1-3,5</c> or an array of numbers.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The selection as written, or <see langword="null"/>.</returns>
    public string GetPages(string name = "pages")
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Array => string.Join(',', element.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString(),
            _ => null,
        };
    }

    /// <summary>
    /// Reads an object or array argument into a model.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <returns>The model, or <see langword="null"/> when the argument is missing or malformed.</returns>
    public T Get<T>(string name)
        where T : class
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        try
        {
            // A model sometimes sends an object as a JSON string.
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();

                if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] is not ('{' or '['))
                {
                    return null;
                }

                using var parsed = JsonDocument.Parse(text);

                return PdfDefinitionJson.FromElement<T>(parsed.RootElement.Clone());
            }

            return PdfDefinitionJson.FromElement<T>(element);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an argument as JSON.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="element">The value.</param>
    /// <returns><see langword="true"/> when the argument is present and not null.</returns>
    public bool TryGetElement(string name, out JsonElement element)
    {
        element = default;

        if (!_arguments.TryGetValue(name, out var value) || value is null)
        {
            return false;
        }

        if (value is JsonElement existing)
        {
            element = existing;

            return existing.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        }

        try
        {
            element = JsonSerializer.SerializeToElement(value);

            return element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
