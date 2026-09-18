using CrestApps.Core.AI.Mcp.Knowledge.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Knowledge;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ingested figure MCP resource type, so a client can read the picture behind a figure a search
    /// returned.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    public static IServiceCollection AddCoreAIKnowledgeMcpResources(this IServiceCollection services, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreAIMcpResourceType<DataSourceFigureResourceHandler>(DataSourceFigureResourceConstants.Type, entry =>
        {
            entry.DisplayName = new LocalizedString("Data Source Figure", "Data Source Figure");
            entry.Description = new LocalizedString("Data Source Figure Description", "Reads the stored picture of a figure or chart in an ingested data source.");
            entry.SupportedVariables =
            [
                new McpResourceVariable("dataSourceId")
                {
                    Description = new LocalizedString("Data Source Id", "The data source the figure belongs to."),
                },
                new McpResourceVariable("figureId")
                {
                    Description = new LocalizedString("Figure Id", "The canonical identifier of the figure or chart."),
                },
            ];

            configure?.Invoke(entry);
        });
    }

    /// <summary>
    /// Adds the ingested figure MCP resource type.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsMcpServerBuilder AddKnowledgeResources(this CrestAppsMcpServerBuilder builder, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIKnowledgeMcpResources(configure);

        return builder;
    }
}
