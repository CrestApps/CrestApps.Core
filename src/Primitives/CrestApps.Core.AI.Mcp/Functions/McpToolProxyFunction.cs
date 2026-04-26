using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Functions;

internal sealed class McpToolProxyFunction : AIFunction
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;
    private readonly string _connectionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolProxyFunction"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="jsonSchema">The json schema.</param>
    /// <param name="connectionId">The connection id.</param>
    public McpToolProxyFunction(
        string name,
        string description,
        JsonElement jsonSchema,
        string connectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        _name = name;
        _description = description ?? name;
        _jsonSchema = jsonSchema;
        _connectionId = connectionId;
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(arguments.Services);

        var logger = arguments.Services.GetRequiredService<ILogger<McpToolProxyFunction>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        var store = arguments.Services.GetRequiredService<ISourceCatalog<McpConnection>>();
        var connection = await store.FindByIdAsync(_connectionId, cancellationToken);

        if (connection is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: MCP connection '{ConnectionId}' not found.", Name, _connectionId);

            return JsonSerializer.Serialize(new { error = $"MCP connection '{_connectionId}' not found." });
        }

        var mcpService = arguments.Services.GetRequiredService<McpService>();
        var client = await mcpService.GetOrCreateClientAsync(connection);

        if (client is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: could not connect to MCP server '{ConnectionId}'.", Name, _connectionId);

            return JsonSerializer.Serialize(new { error = $"Failed to connect to MCP server '{_connectionId}'." });
        }

        try
        {
            var args = new Dictionary<string, object>();

            foreach (var kvp in arguments)
            {
                if (kvp.Value is JsonElement jsonElement)
                {
                    args[kvp.Key] = ConvertJsonElement(jsonElement);
                }
                else if (kvp.Value is not null)
                {
                    args[kvp.Key] = kvp.Value;
                }
            }

            var result = await client.CallToolAsync(_name, args, cancellationToken: cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking MCP tool '{ToolName}' on server '{ConnectionId}'.", _name, _connectionId);

            return JsonSerializer.Serialize(new { error = $"Error invoking MCP tool '{_name}'." });
        }
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
