using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static IMcpServerBuilder WithCrestAppsHandlers(this IMcpServerBuilder builder)
    {
        return builder.WithCrestAppsHandlers(configure: null);
    }

    /// <summary>
    /// Registers the CrestApps MCP server handlers, letting the caller choose which capabilities
    /// (tools, prompts, and resources) are exposed and which tools are listed and invokable. When
    /// <paramref name="configure"/> is <see langword="null"/> every capability is registered and all
    /// non-hidden tools are exposed, preserving the previous default behavior.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">A delegate that configures the exposed capabilities and tool filters.</param>
    public static IMcpServerBuilder WithCrestAppsHandlers(
        this IMcpServerBuilder builder,
        Action<CrestAppsMcpHandlerBuilder> configure)
    {
        return builder.WithCrestAppsHandlers(configuration: null, configure);
    }

    /// <summary>
    /// Registers the CrestApps MCP server handlers, binding the capability toggles from the supplied
    /// configuration section so a host can enable or disable capabilities without code. Configuration
    /// wins over code: any capability toggle explicitly set in <paramref name="configuration"/>
    /// overrides the value chosen in <paramref name="configure"/>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configuration">
    /// The configuration section bound to <see cref="McpServerHandlerOptions"/>. When
    /// <see langword="null"/> no configuration binding is applied.
    /// </param>
    /// <param name="configure">A delegate that configures the exposed capabilities and tool filters.</param>
    public static IMcpServerBuilder WithCrestAppsHandlers(
        this IMcpServerBuilder builder,
        IConfiguration configuration,
        Action<CrestAppsMcpHandlerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var handlerBuilder = new CrestAppsMcpHandlerBuilder();
        configure?.Invoke(handlerBuilder);

        if (configuration is not null)
        {
            var options = new McpServerHandlerOptions();
            configuration.Bind(options);
            handlerBuilder.ApplyOptions(options);

            builder.Services.Configure<McpServerHandlerOptions>(configuration);
        }

        return builder.WithCrestAppsHandlers(handlerBuilder);
    }

    private static IMcpServerBuilder WithCrestAppsHandlers(
        this IMcpServerBuilder builder,
        CrestAppsMcpHandlerBuilder handlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (handlerBuilder.IncludeTools)
        {
            var includeSdkTools = handlerBuilder.IncludeSdkTools;

            builder
                .WithListToolsHandler((request, cancellationToken) =>
                {
                    var toolDefinitions = request.Services.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
                    ILogger logger = null;
                    var tools = new List<Tool>();

                    foreach (var (name, _) in toolDefinitions.Tools.Where(tool => !tool.Value.Hidden && handlerBuilder.IsToolAllowed(tool.Key, tool.Value)))
                    {
                        try
                        {
                            if (request.Services.GetKeyedService<AITool>(name) is AIFunction aiFunction)
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

                    if (includeSdkTools)
                    {
                        var sdkTools = request.Services.GetService<IEnumerable<McpServerTool>>();

                        if (sdkTools is not null)
                        {
                            using var sdkToolEnumerator = sdkTools.GetEnumerator();

                            if (sdkToolEnumerator.MoveNext())
                            {
                                var toolNames = new HashSet<string>(tools.Count, StringComparer.Ordinal);

                                foreach (var tool in tools)
                                {
                                    toolNames.Add(tool.Name);
                                }

                                do
                                {
                                    var sdkTool = sdkToolEnumerator.Current;

                                    if (toolNames.Add(sdkTool.ProtocolTool.Name))
                                    {
                                        tools.Add(sdkTool.ProtocolTool);
                                    }
                                }
                                while (sdkToolEnumerator.MoveNext());
                            }
                        }
                    }

                    return ValueTask.FromResult(new ListToolsResult { Tools = tools });
                })
                .WithCallToolHandler(async (request, cancellationToken) =>
                {
                    var toolDefinitions = request.Services.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;

                    if (toolDefinitions.Tools.TryGetValue(request.Params.Name, out var definition) &&
                        !definition.Hidden &&
                        handlerBuilder.IsToolAllowed(request.Params.Name, definition))
                    {
                        if (request.Services.GetKeyedService<AITool>(request.Params.Name) is not AIFunction aiFunction)
                        {
                            throw new McpException($"Failed to create tool '{request.Params.Name}'.");
                        }

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

                        var result = await aiFunction.InvokeAsync(arguments, cancellationToken);

                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = result?.ToString() ?? string.Empty }],
                        };
                    }

                    if (includeSdkTools)
                    {
                        var sdkTools = request.Services.GetService<IEnumerable<McpServerTool>>();
                        var sdkTool = sdkTools?.FirstOrDefault(t => t.ProtocolTool.Name == request.Params.Name);

                        if (sdkTool is not null)
                        {
                            return await sdkTool.InvokeAsync(request, cancellationToken);
                        }
                    }

                    throw new McpException($"Tool '{request.Params.Name}' not found.");
                });
        }

        if (handlerBuilder.IncludePrompts)
        {
            builder
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
                });
        }

        if (handlerBuilder.IncludeResources)
        {
            builder
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

        return builder;
    }
}
