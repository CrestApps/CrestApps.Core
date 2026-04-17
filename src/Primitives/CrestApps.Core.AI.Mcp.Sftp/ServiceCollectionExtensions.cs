using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Sftp.Handlers;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Sftp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAISftpMcpResources(this IServiceCollection services, Action<McpResourceTypeEntry> configure = null)
    {
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

    public static CrestAppsMcpServerBuilder AddSftpResources(this CrestAppsMcpServerBuilder builder, Action<McpResourceTypeEntry> configure = null)
    {
        builder.Services.AddCoreAISftpMcpResources(configure);
        return builder;
    }

}
