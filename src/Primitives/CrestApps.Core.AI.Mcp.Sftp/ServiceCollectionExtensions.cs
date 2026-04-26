using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Sftp.Handlers;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Sftp;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
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
