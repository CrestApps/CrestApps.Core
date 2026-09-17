using CrestApps.Core.AI.Indexers;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Sftp.Handlers;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Registers the two ways this package talks to an SFTP server: as a file source ingestion reads, and as
/// an MCP resource a model reads.
/// </summary>
/// <remarks>
/// The two used to ship as separate packages, which meant the connection settings both read lived in the
/// MCP one and the ingestion package depended on it to get at them. They register independently -- taking
/// the SFTP file source does not start an MCP server, and exposing SFTP resources does not enable ingestion.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SFTP ingestion connector.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreSftpIngestionConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<SftpRemoteFileClientFactory>();

        return services.AddCoreIngestionConnector<SftpIngestionConnector>(
            SftpIngestionConnector.ConnectorName,
            descriptor =>
            {
                descriptor.DisplayName = new LocalizedString("Sftp", "SFTP");
                descriptor.Description = new LocalizedString("Sftp Description", "Reads files from an SFTP server.");
            });
    }

    /// <summary>
    /// Adds core ai sftp mcp resources.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    public static IServiceCollection AddCoreAISftpMcpResources(this IServiceCollection services, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreAIMcpResourceType<SftpResourceTypeHandler>(SftpResourceConstants.Type, entry =>
                {
                    entry.DisplayName = new LocalizedString("SFTP", "SFTP");
                    entry.Description = new LocalizedString("SFTP Description", "Reads content from SFTP servers.");
                    entry.SupportedVariables = [new McpResourceVariable("path")
            {
                Description = new LocalizedString("SFTP Path", "The remote file path on the SFTP server.")
            }, ];
                    configure?.Invoke(entry);
                });
    }

    /// <summary>
    /// Adds sftp resources.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsMcpServerBuilder AddSftpResources(this CrestAppsMcpServerBuilder builder, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAISftpMcpResources(configure);

        return builder;
    }
}
