using System.Text.Json;
using CrestApps.Core.AI.Mcp.Functions;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Services;

internal sealed class McpToolRegistryProvider : IToolRegistryProvider
{
    private static readonly JsonElement _emptySchema = JsonSerializer.Deserialize<JsonElement>(
        """{"type": "object", "properties": {}, "additionalProperties": false}""");

    private readonly IMcpServerMetadataCacheProvider _metadataProvider;
    private readonly ISourceCatalog<McpConnection> _store;
    private readonly ILogger<McpToolRegistryProvider> _logger;

    public McpToolRegistryProvider(
        IMcpServerMetadataCacheProvider metadataProvider,
        ISourceCatalog<McpConnection> store,
        ILogger<McpToolRegistryProvider> logger)
    {
        _metadataProvider = metadataProvider;
        _store = store;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var mcpConnectionIds = context?.McpConnectionIds;

        if (mcpConnectionIds is null || mcpConnectionIds.Length == 0)
        {
            return [];
        }

        var connections = await _store.GetAsync(mcpConnectionIds);

        if (connections.Count == 0)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var connection in connections)
        {
            try
            {
                var capabilities = await _metadataProvider.GetCapabilitiesAsync(connection);

                if (capabilities?.Tools is null || capabilities.Tools.Count == 0)
                {
                    continue;
                }

                var connectionId = connection.ItemId;

                foreach (var tool in capabilities.Tools)
                {
                    if (string.IsNullOrWhiteSpace(tool.Name))
                    {
                        continue;
                    }

                    var toolName = tool.Name;
                    var toolDescription = tool.Description ?? toolName;
                    var toolSchema = tool.InputSchema ?? _emptySchema;

                    entries.Add(new ToolRegistryEntry
                    {
                        Id = $"mcp:{connectionId}:{toolName}",
                        Name = toolName,
                        Description = toolDescription,
                        Source = ToolRegistryEntrySource.McpServer,
                        SourceId = connectionId,
                        CreateAsync = _ => ValueTask.FromResult<AITool>(
                            new McpToolProxyFunction(toolName, toolDescription, toolSchema, connectionId)),
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load MCP tool metadata from connection '{ConnectionId}'. Skipping.",
                    connection.ItemId);
            }
        }

        return entries;
    }
}
