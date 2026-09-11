using System.Text.Json;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
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
    /// The input schema advertised for an agent exposed as a tool. It mirrors the schema
    /// <c>AgentProxyTool</c> presents to the model, so an MCP client calls an agent exactly the way the
    /// orchestrator does.
    /// </summary>
    private static readonly JsonElement AgentInputSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "prompt": {
          "type": "string",
          "description": "The prompt or message to send to the agent for processing."
        }
      },
      "required": ["prompt"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Registers the standard CrestApps MCP server handlers for tools, prompts, and resources.
    /// This wires the CrestApps tool registry (<see cref="AIToolDefinitionOptions"/>),
    /// <see cref="IMcpServerPromptService"/>, and <see cref="IMcpServerResourceService"/>
    /// into the MCP protocol so both Orchard Core and standalone MVC hosts share the same handler logic.
    /// Only selectable tools (those that are neither system tools nor hidden) are ever exposed, so
    /// system tools that the orchestrator auto-includes are never listed or callable over MCP.
    /// Which of those selectable tools and tool instances are actually listed and callable is further
    /// controlled by the <see cref="McpServerOptions"/> site settings allow-list.
    /// Agent profiles named in <see cref="McpServerOptions.Agents"/> are exposed as tools too, under their
    /// own switch so enabling every tool never silently enables every agent. Invoking one runs that agent
    /// with the tools its own profile configures, which the tool allow-list neither grants nor restricts.
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

                // Agents are enumerated last so a name they share with a tool or instance resolves to that
                // tool, matching the call handler's resolution order.
                foreach (var agent in await GetAllowedAgentsAsync(request.Services, serverOptions, cancellationToken))
                {
                    if (!seenNames.Add(agent.Name))
                    {
                        // A name collision is a configuration mistake, not an exceptional one, so it is
                        // resolved and logged rather than allowed to fail the whole listing.
                        logger ??= request.Services.GetService<ILogger<IMcpServerPromptService>>();
                        logger?.LogWarning(
                            "Agent '{AgentName}' is not exposed over MCP because a tool of the same name is already exposed.",
                            agent.Name);

                        continue;
                    }

                    tools.Add(new Tool
                    {
                        Name = agent.Name,
                        Description = agent.Description,
                        InputSchema = AgentInputSchema,
                    });
                }

                return new ListToolsResult { Tools = tools };
            })
            .WithCallToolHandler(async (request, cancellationToken) =>
            {
                // Tools invoked over MCP run outside any completion, so nothing has established the ambient
                // invocation scope they expect. Without it an agent silently runs with its tools disabled
                // (AgentProxyTool treats an untrackable depth as unsafe) and citation-emitting tools fall back
                // to local reference numbering. Beginning a scope here makes an MCP call behave like the
                // top-level completion it stands in for.
                using var invocationScope = AIInvocationScope.Begin();

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

                var agent = (await GetAllowedAgentsAsync(request.Services, serverOptions, cancellationToken))
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, request.Params.Name, StringComparison.OrdinalIgnoreCase));

                if (agent is not null)
                {
                    // The agent runs its own configured tools. The allow-list governs which agents a client
                    // may invoke, never what an agent uses internally to do its job — filtering those would
                    // force an operator to expose each of them directly, which is the opposite of the point.
                    var agentTool = new AgentProxyTool(agent.Name, agent.Description);
                    var agentResult = await agentTool.InvokeAsync(BuildArguments(request), cancellationToken);

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = agentResult?.ToString() ?? string.Empty }],
                    };
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

    /// <summary>
    /// Gets the agent profiles this server is configured to expose, in a stable order.
    /// </summary>
    /// <remarks>
    /// An agent needs a name and a description to be exposed at all: the description is the only signal an
    /// MCP client has for what the agent is for, and an agent nobody can tell apart is worse than an absent
    /// one. Availability (<c>AlwaysAvailable</c> versus <c>OnDemand</c>) is deliberately ignored — that
    /// governs a completion's token budget, not who may reach the agent from outside.
    /// </remarks>
    /// <param name="services">The request services.</param>
    /// <param name="serverOptions">The server exposure settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exposable agent profiles.</returns>
    private static async Task<IReadOnlyList<AIProfile>> GetAllowedAgentsAsync(
        IServiceProvider services,
        McpServerOptions serverOptions,
        CancellationToken cancellationToken)
    {
        var exposeAllAgents = serverOptions.ExposeAllAgents;

        if (!exposeAllAgents && serverOptions.Agents is not { Count: > 0 })
        {
            return [];
        }

        var profileManager = services.GetService<IAIProfileManager>();

        if (profileManager is null)
        {
            return [];
        }

        var agents = await profileManager.GetAsync(AIProfileType.Agent, cancellationToken);

        if (agents is null)
        {
            return [];
        }

        var allowList = exposeAllAgents ? null : BuildAllowList(serverOptions.Agents);

        return agents
            .Where(agent => !string.IsNullOrEmpty(agent.Name) &&
                !string.IsNullOrEmpty(agent.Description) &&
                IsAllowed(exposeAllAgents, allowList, agent.Name))
            .ToList();
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
