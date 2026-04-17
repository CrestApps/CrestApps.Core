using CrestApps.Core.AI.Mcp.Ftp.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Ftp;

public static class ServiceCollectionExtensions
{
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

    public static CrestAppsMcpServerBuilder AddFtpResources(this CrestAppsMcpServerBuilder builder, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIFtpMcpResources(configure);
        return builder;
    }
}
