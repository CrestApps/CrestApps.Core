using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Word.Workspace;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Reads a Word tool's arguments whatever shape they arrive in — JSON elements, CLR values, or numbers and
/// booleans a model wrote as strings.
/// </summary>
internal sealed class WordToolArguments
{
    private readonly AIFunctionArguments _arguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordToolArguments"/> class.
    /// </summary>
    /// <param name="arguments">The raw arguments.</param>
    public WordToolArguments(AIFunctionArguments arguments)
    {
        _arguments = arguments;
    }

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services => _arguments.Services;

    /// <summary>
    /// Reads the document the call names, from its <c>document</c> argument.
    /// </summary>
    /// <returns>The name, or <see langword="null"/> for the active document.</returns>
    public string Document()
    {
        return GetString("document");
    }

    /// <summary>
    /// Reads the name the result is saved under, from its <c>save_as</c> argument.
    /// </summary>
    /// <returns>The name, or <see langword="null"/> to keep the document's own.</returns>
    public string SaveAs()
    {
        return GetString("save_as");
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
    /// Reads a string argument without trimming or dropping blank values, for text whose spacing matters.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public string GetRawString(string name)
    {
        if (!TryGetElement(name, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
    }

    /// <summary>
    /// Reads a boolean argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public bool? GetBoolean(string name)
    {
        return TryGetElement(name, out var element) ? WordJsonValues.ReadBoolean(element) : null;
    }

    /// <summary>
    /// Reads a number argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or not a number.</returns>
    public double? GetDouble(string name)
    {
        return TryGetElement(name, out var element) ? WordJsonValues.ReadDouble(element) : null;
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
    /// <param name="splitCommas">Whether a single string is split at commas, for lists of ids or names.</param>
    /// <returns>The values, or an empty list.</returns>
    public List<string> GetStrings(string name, bool splitCommas = false)
    {
        return TryGetElement(name, out var element) ? WordJsonValues.ReadStrings(element, splitCommas) : [];
    }

    /// <summary>
    /// Reads element ids from an argument that holds one id or a list of them.
    /// </summary>
    /// <param name="names">The argument names to read, in order, such as <c>ids</c> and <c>id</c>.</param>
    /// <returns>The ids, normalized, or an empty list.</returns>
    public List<string> GetIds(params string[] names)
    {
        var ids = new List<string>();

        foreach (var name in names)
        {
            foreach (var value in GetStrings(name, splitCommas: true))
            {
                var normalized = OpenXml.Word.WordParagraphIds.Normalize(value);

                if (!string.IsNullOrEmpty(normalized) && !ids.Contains(normalized, StringComparer.Ordinal))
                {
                    ids.Add(normalized);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Reads an object or array argument into a model.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <returns>The model, or <see langword="null"/> when the argument is missing.</returns>
    /// <exception cref="WordToolException">The argument is not shaped like the model.</exception>
    public T Get<T>(string name)
        where T : class
    {
        if (!TryGetObject(name, out var element))
        {
            return null;
        }

        try
        {
            return element.Deserialize<T>(WordJson.Options);
        }
        catch (JsonException ex)
        {
            throw new WordToolException($"The '{name}' argument could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads an object or array argument as JSON, accepting one a model sent as a JSON string.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="element">The value.</param>
    /// <returns><see langword="true"/> when the argument is present and is an object or array.</returns>
    public bool TryGetObject(string name, out JsonElement element)
    {
        if (!TryGetElement(name, out element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();

            if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] is not ('{' or '['))
            {
                return false;
            }

            try
            {
                using var parsed = JsonDocument.Parse(text);

                element = parsed.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new WordToolException($"The '{name}' argument is not valid JSON: {ex.Message}", ex);
            }
        }

        return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
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
    /// Formats a number the way answers write it.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number, invariant.</returns>
    public static string Invariant(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
