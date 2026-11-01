using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Checks the page tools a browser tab reports before a model may use them. The browser is not trusted: a tool whose
/// name WebMCP would refuse, or whose description or schema is missing, broken or too large, is dropped. Every valid
/// WebMCP name gets a function name AI providers accept.
/// </summary>
internal static partial class WebMcpPageToolValidator
{
    /// <summary>
    /// The most tools a browser tab may offer.
    /// </summary>
    public const int MaxTools = 64;

    /// <summary>
    /// The longest description a tool may have, in characters.
    /// </summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// The largest input schema a tool may have, in characters of JSON.
    /// </summary>
    public const int MaxSchemaLength = 16 * 1024;

    /// <summary>
    /// The longest function name AI providers accept.
    /// </summary>
    public const int MaxFunctionNameLength = 64;

    private static readonly JsonElement _emptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    /// <summary>
    /// Keeps the valid tools, the first of each function name, up to <see cref="MaxTools"/>.
    /// </summary>
    /// <param name="descriptors">The tools the browser reports.</param>
    /// <returns>The valid tools.</returns>
    public static IReadOnlyList<WebMcpPageTool> Validate(IEnumerable<WebMcpPageToolDescriptor> descriptors)
    {
        var tools = new List<WebMcpPageTool>();

        if (descriptors is null)
        {
            return tools;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            if (tools.Count >= MaxTools)
            {
                break;
            }

            if (descriptor is null ||
                string.IsNullOrEmpty(descriptor.Name) ||
                !WebMcpToolNameRegex().IsMatch(descriptor.Name) ||
                string.IsNullOrWhiteSpace(descriptor.Description) ||
                descriptor.Description.Length > MaxDescriptionLength ||
                !TryParseSchema(descriptor.InputSchema, out var schema))
            {
                continue;
            }

            var functionName = ToFunctionName(descriptor.Name);

            if (!seenNames.Add(functionName))
            {
                continue;
            }

            tools.Add(new WebMcpPageTool
            {
                Name = descriptor.Name,
                FunctionName = functionName,
                ReadOnly = descriptor.ReadOnly,
                Consequential = descriptor.Consequential,
                UntrustedContent = descriptor.UntrustedContent,
                Title = string.IsNullOrWhiteSpace(descriptor.Title)
                    ? descriptor.Name
                    : descriptor.Title.Trim(),
                Description = descriptor.Description.Trim(),
                InputSchema = schema,
            });
        }

        return tools;
    }

    /// <summary>
    /// Turns a WebMCP tool name into a function name AI providers accept: characters outside letters, digits, '_' and
    /// '-' become '_', and a name longer than <see cref="MaxFunctionNameLength"/> keeps its start and ends with a hash
    /// of the whole name, so it stays the same for the same tool and differs between tools.
    /// </summary>
    /// <param name="name">The WebMCP tool name.</param>
    /// <returns>The function name.</returns>
    public static string ToFunctionName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        if (builder.Length <= MaxFunctionNameLength)
        {
            return builder.ToString();
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)), 0, 4);

        return builder.ToString(0, MaxFunctionNameLength - hash.Length - 1) + "_" + hash;
    }

    private static bool TryParseSchema(string json, out JsonElement schema)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            schema = _emptySchema;

            return true;
        }

        schema = default;

        if (json.Length > MaxSchemaLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            schema = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex WebMcpToolNameRegex();
}
