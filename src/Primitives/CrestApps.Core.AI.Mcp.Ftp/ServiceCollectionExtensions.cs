using CrestApps.Core.AI.Mcp.Ftp.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Ftp;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
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
