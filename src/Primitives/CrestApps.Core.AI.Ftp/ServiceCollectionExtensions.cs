using CrestApps.Core.AI.Ftp.Handlers;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Registers the two ways this package talks to an FTP server: as a file source ingestion reads, and as
/// an MCP resource a model reads.
/// </summary>
/// <remarks>
/// The two used to ship as separate packages, which meant the connection settings both read lived in the
/// MCP one and the ingestion package depended on it to get at them. They register independently -- taking
/// the FTP file source does not start an MCP server, and exposing FTP resources does not enable ingestion.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the FTP/FTPS ingestion connector.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreFtpIngestionConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<FtpRemoteFileClientFactory>();

        return services.AddCoreIngestionConnector<FtpIngestionConnector>(
            FtpIngestionConnector.ConnectorName,
            descriptor =>
            {
                descriptor.DisplayName = new LocalizedString("Ftp", "FTP / FTPS");
                descriptor.Description = new LocalizedString("Ftp Description", "Reads files from an FTP or FTPS server.");
            });
    }

    /// <summary>
    /// Adds core ai ftp mcp resources.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    public static IServiceCollection AddCoreAIFtpMcpResources(this IServiceCollection services, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreAIMcpResourceType<FtpResourceTypeHandler>(FtpResourceConstants.Type, entry =>
                {
                    entry.DisplayName = new LocalizedString("FTP", "FTP/FTPS");
                    entry.Description = new LocalizedString("FTP Description", "Reads content from FTP/FTPS servers.");
                    entry.SupportedVariables = [new McpResourceVariable("path")
            {
                Description = new LocalizedString("FTP Path", "The remote file path on the FTP server.")
            }, ];
                    configure?.Invoke(entry);
                });
    }

    /// <summary>
    /// Adds ftp resources.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsMcpServerBuilder AddFtpResources(this CrestAppsMcpServerBuilder builder, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIFtpMcpResources(configure);

        return builder;
    }
}
