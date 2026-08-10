using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpServerOptions = CrestApps.Core.AI.Mcp.Models.McpServerOptions;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Provides extension methods for MCP Server Builder.
/// </summary>
public static class McpServerBuilderExtensions
{
    /// <summary>
    /// Registers the standard CrestApps MCP server handlers for tools, prompts, and resources.
    /// This wires the CrestApps tool registry (<see cref="AIToolDefinitionOptions"/>),
    /// <see cref="IMcpServerPromptService"/>, and <see cref="IMcpServerResourceService"/>
    /// into the MCP protocol so both Orchard Core and standalone MVC hosts share the same handler logic.
    /// Only selectable tools (those that are neither system tools nor hidden) are ever exposed, so
    /// system tools that the orchestrator auto-includes are never listed or callable over MCP.
    /// Which of those selectable tools and tool instances are actually listed and callable is further
    /// controlled by the <see cref="McpServerOptions"/> site settings allow-list.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static IMcpServerBuilder WithCrestAppsHandlers(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithListToolsHandler(async (request, cancellationToken) =>
            {
                var serverOptions = request.Services.GetRequiredService<IOptionsMonitor<McpServerOptions>>().CurrentValue;
                var exposeAll = serverOptions.ExposeAllTools;
                var allowList = exposeAll ? null : BuildAllowList(serverOptions.Tools);
                var toolDefinitions = request.Services.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
                ILogger logger = null;
                var tools = new List<Tool>();
                var seenNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (name, definition) in toolDefinitions.Tools)
                {
                    if (!definition.IsSelectable() || !IsAllowed(exposeAll, allowList, name, definition.Name))
                    {
                        continue;
                    }

                    try
                    {
                        if (request.Services.GetKeyedService<AITool>(name) is AIFunction aiFunction && seenNames.Add(aiFunction.Name))
                        {
                            tools.Add(new Tool
                            {
                                Name = aiFunction.Name,
                                Description = aiFunction.Description,
                                InputSchema = aiFunction.JsonSchema,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger ??= request.Services.GetRequiredService<ILogger<IMcpServerPromptService>>();
                        logger.LogError(ex, "Error creating tool instance for '{ToolName}'.", name);
                    }
                }

                var instanceCatalog = request.Services.GetService<INamedCatalog<AIToolInstance>>();

                if (instanceCatalog is not null)
                {
                    var instances = await instanceCatalog.GetAllAsync(cancellationToken);

                    foreach (var instance in instances)
                    {
                        if (string.IsNullOrEmpty(instance.Source))
                        {
                            continue;
                        }

                        var functionName = instance.GetFunctionName();

                        if (!IsAllowed(exposeAll, allowList, functionName, instance.Name))
                        {
                            continue;
                        }

                        var source = request.Services.GetKeyedService<IAIToolInstanceSource>(instance.Source);

                        if (source is null)
                        {
                            continue;
                        }

                        try
                        {
                            if (source.CreateTool(instance) is AIFunction aiFunction && seenNames.Add(aiFunction.Name))
                            {
                                tools.Add(new Tool
                                {
                                    Name = aiFunction.Name,
                                    Description = aiFunction.Description,
                                    InputSchema = aiFunction.JsonSchema,
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            logger ??= request.Services.GetRequiredService<ILogger<IMcpServerPromptService>>();
                            logger.LogError(ex, "Error creating tool for instance '{InstanceName}'.", instance.Name);
                        }
                    }
                }

                return new ListToolsResult { Tools = tools };
            })
            .WithCallToolHandler(async (request, cancellationToken) =>
            {
                var serverOptions = request.Services.GetRequiredService<IOptionsMonitor<McpServerOptions>>().CurrentValue;
                var exposeAll = serverOptions.ExposeAllTools;
                var allowList = exposeAll ? null : BuildAllowList(serverOptions.Tools);
                var toolDefinitions = request.Services.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;

                var logger = request.Services.GetService<ILogger<IMcpServerPromptService>>();
                var codeTool = ResolveAllowedCodeTool(request.Services, toolDefinitions, exposeAll, allowList, request.Params.Name, logger);

                if (codeTool is not null)
                {
                    var result = await codeTool.InvokeAsync(BuildArguments(request), cancellationToken);

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = result?.ToString() ?? string.Empty }],
                    };
                }

                var instanceCatalog = request.Services.GetService<INamedCatalog<AIToolInstance>>();

                if (instanceCatalog is not null)
                {
                    var instance = await ResolveInstanceAsync(instanceCatalog, request.Params.Name, cancellationToken);

                    if (instance is not null &&
                        !string.IsNullOrEmpty(instance.Source) &&
                        IsAllowed(exposeAll, allowList, instance.GetFunctionName(), instance.Name))
                    {
                        var source = request.Services.GetKeyedService<IAIToolInstanceSource>(instance.Source);

                        if (source is not null && source.CreateTool(instance) is AIFunction instanceFunction)
                        {
                            var result = await instanceFunction.InvokeAsync(BuildArguments(request), cancellationToken);

                            return new CallToolResult
                            {
                                Content = [new TextContentBlock { Text = result?.ToString() ?? string.Empty }],
                            };
                        }
                    }
                }

                throw new McpException($"Tool '{request.Params.Name}' not found.");
            })
            .WithListPromptsHandler(async (request, cancellationToken) =>
            {
                var promptService = request.Services.GetRequiredService<IMcpServerPromptService>();

                return new ListPromptsResult
                {
                    Prompts = await promptService.ListAsync(),
                };
            })
            .WithGetPromptHandler(async (request, cancellationToken) =>
            {
                var promptService = request.Services.GetRequiredService<IMcpServerPromptService>();

                return await promptService.GetAsync(request, cancellationToken);
            })
            .WithListResourcesHandler(async (request, cancellationToken) =>
            {
                var resourceService = request.Services.GetRequiredService<IMcpServerResourceService>();

                return new ListResourcesResult
                {
                    Resources = await resourceService.ListAsync(),
                };
            })
            .WithListResourceTemplatesHandler(async (request, cancellationToken) =>
            {
                var resourceService = request.Services.GetRequiredService<IMcpServerResourceService>();

                return new ListResourceTemplatesResult
                {
                    ResourceTemplates = await resourceService.ListTemplatesAsync(),
                };
            })
            .WithReadResourceHandler(async (request, cancellationToken) =>
            {
                var resourceService = request.Services.GetRequiredService<IMcpServerResourceService>();

                return await resourceService.ReadAsync(request, cancellationToken);
            });
    }

    private static HashSet<string> BuildAllowList(IEnumerable<string> names)
    {
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (names is not null)
        {
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    allowList.Add(name.Trim());
                }
            }
        }

        return allowList;
    }

    private static bool IsAllowed(bool exposeAll, HashSet<string> allowList, params string[] candidates)
    {
        if (exposeAll)
        {
            return true;
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && allowList.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static AIFunction ResolveAllowedCodeTool(
        IServiceProvider services,
        AIToolDefinitionOptions toolDefinitions,
        bool exposeAll,
        HashSet<string> allowList,
        string protocolName,
        ILogger logger)
    {
        if (toolDefinitions.Tools.TryGetValue(protocolName, out var direct) &&
            direct.IsSelectable() &&
            IsAllowed(exposeAll, allowList, protocolName, direct.Name) &&
            TryCreateFunction(services, protocolName, logger) is { } directFunction &&
            string.Equals(directFunction.Name, protocolName, StringComparison.Ordinal))
        {
            return directFunction;
        }

        foreach (var (name, definition) in toolDefinitions.Tools)
        {
            if (!definition.IsSelectable() || !IsAllowed(exposeAll, allowList, name, definition.Name))
            {
                continue;
            }

            if (TryCreateFunction(services, name, logger) is { } function &&
                string.Equals(function.Name, protocolName, StringComparison.Ordinal))
            {
                return function;
            }
        }

        return null;
    }

    private static AIFunction TryCreateFunction(IServiceProvider services, string key, ILogger logger)
    {
        try
        {
            return services.GetKeyedService<AITool>(key) as AIFunction;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error creating tool '{ToolName}'.", key);

            return null;
        }
    }

    private static async Task<AIToolInstance> ResolveInstanceAsync(
        INamedCatalog<AIToolInstance> catalog,
        string name,
        CancellationToken cancellationToken)
    {
        var instances = await catalog.GetAllAsync(cancellationToken);

        foreach (var instance in instances)
        {
            if (string.Equals(instance.GetFunctionName(), name, StringComparison.Ordinal) ||
                (instance.Name is not null && string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return instance;
            }
        }

        return null;
    }

    private static AIFunctionArguments BuildArguments(RequestContext<CallToolRequestParams> request)
    {
        var arguments = new AIFunctionArguments
        {
            Services = request.Services,
            Context = new Dictionary<object, object>
            {
                ["mcpRequest"] = request,
            },
        };

        if (request.Params.Arguments is not null)
        {
            foreach (var kvp in request.Params.Arguments)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        return arguments;
    }
}
