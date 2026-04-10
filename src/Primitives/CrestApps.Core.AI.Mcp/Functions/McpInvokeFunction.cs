using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp.Functions;

internal sealed class McpInvokeFunction : AIFunction
{
    public const string FunctionName = "mcp_invoke";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "clientId": {
          "type": "string",
          "description": "The MCP server connection identifier."
        },
        "type": {
          "type": "string",
          "enum": ["tool", "prompt", "resource"],
          "description": "The type of MCP capability to invoke."
        },
        "id": {
          "type": "string",
          "description": "For tools and prompts, this is the capability name. For resources, this MUST be the fully-resolved resource URI."
        },
        "inputs": {
          "type": "object",
          "description": "The input arguments for the invocation. For tools, these MUST match the tool's Parameters schema exactly."
        }
      },
      "required": ["clientId", "type", "id"],
      "additionalProperties": false
    }
    """);

    public override string Name => FunctionName;
    public override string Description => "Invoke an MCP server capability (tool, prompt, or resource) by specifying the server, capability type, and identifier.";
    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(arguments.Services);

        var logger = arguments.Services.GetRequiredService<ILogger<McpInvokeFunction>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        var clientId = GetRequiredStringArgument(arguments, "clientId");
        var type = GetRequiredStringArgument(arguments, "type");
        var id = GetRequiredStringArgument(arguments, "id");
        var inputs = GetOptionalObjectArgument(arguments, "inputs");

        var store = arguments.Services.GetRequiredService<ISourceCatalog<McpConnection>>();
        var connection = await store.FindByIdAsync(clientId);

        if (connection is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: MCP connection '{ClientId}' not found.", Name, clientId);
            return JsonSerializer.Serialize(new { error = $"MCP connection '{clientId}' not found." });
        }

        var mcpService = arguments.Services.GetRequiredService<McpService>();
        var client = await mcpService.GetOrCreateClientAsync(connection);

        if (client is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: could not connect to MCP server '{ClientId}'.", Name, clientId);
            return JsonSerializer.Serialize(new { error = $"Failed to connect to MCP server '{clientId}'." });
        }

        try
        {
            var content = type.ToLowerInvariant() switch
            {
                "tool" => await InvokeToolAsync(client, id, inputs, cancellationToken),
                "prompt" => await InvokePromptAsync(client, id, inputs, cancellationToken),
                "resource" => await InvokeResourceAsync(client, id, cancellationToken),
                _ => JsonSerializer.Serialize(new { error = $"Unknown capability type '{type}'. Use 'tool', 'prompt', or 'resource'." }),
            };

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking MCP capability '{Type}/{Id}' on server '{ClientId}'.", type, id, clientId);
            return JsonSerializer.Serialize(new { error = "Error invoking MCP capability." });
        }
    }

    private static async Task<object> InvokeToolAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, object>();

        if (inputs is not null)
        {
            foreach (var kvp in inputs)
            {
                args[kvp.Key] = kvp.Value is JsonElement jsonElement ? ConvertJsonElement(jsonElement) : kvp.Value;
            }
        }

        var result = await client.CallToolAsync(toolName, args, cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(result);
    }

    private static async Task<object> InvokePromptAsync(
        McpClient client,
        string promptName,
        Dictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object> args = null;

        if (inputs is not null && inputs.Count > 0)
        {
            args = inputs;
        }

        var result = await client.GetPromptAsync(promptName, args, cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(result);
    }

    private static async Task<object> InvokeResourceAsync(McpClient client, string resourceUri, CancellationToken cancellationToken)
    {
        if (!resourceUri.Contains("://", StringComparison.Ordinal))
        {
            var resolvedUri = await TryResolveResourceUriByNameAsync(client, resourceUri, cancellationToken);

            if (resolvedUri is not null)
            {
                resourceUri = resolvedUri;
            }
        }

        var result = await client.ReadResourceAsync(resourceUri, cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> TryResolveResourceUriByNameAsync(McpClient client, string name, CancellationToken cancellationToken)
    {
        try
        {
            var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken);
            var match = resources.FirstOrDefault(resource => string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase));
            return match?.Uri;
        }
        catch
        {
            return null;
        }
    }

    private static string GetRequiredStringArgument(AIFunctionArguments arguments, string name)
    {
        if (arguments.TryGetValue(name, out var value) && value is not null)
        {
            var stringValue = value switch
            {
                string text => text,
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String => jsonElement.GetString(),
                _ => value.ToString(),
            };

            if (!string.IsNullOrEmpty(stringValue))
            {
                return stringValue;
            }
        }

        throw new ArgumentException($"Required argument '{name}' is missing or empty.");
    }

    private static Dictionary<string, object> GetOptionalObjectArgument(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object> dictionary)
        {
            return dictionary;
        }

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, object>();

                foreach (var property in jsonElement.EnumerateObject())
                {
                    result[property.Name] = property.Value;
                }

                return result;
            }

            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using var parsed = JsonDocument.Parse(jsonElement.GetString());

                    if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var result = new Dictionary<string, object>();

                        foreach (var property in parsed.RootElement.EnumerateObject())
                        {
                            result[property.Name] = property.Value;
                        }

                        return result;
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        if (value is string text && text.TrimStart().StartsWith('{'))
        {
            try
            {
                using var parsed = JsonDocument.Parse(text);

                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var result = new Dictionary<string, object>();

                    foreach (var property in parsed.RootElement.EnumerateObject())
                    {
                        result[property.Name] = property.Value;
                    }

                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integerValue) => integerValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
            _ => element.GetRawText(),
        };
    }
}
