using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Indexers.FileTransfer.Ftp;

/// <summary>
/// Registers the FTP/FTPS ingestion connector.
/// </summary>
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
}
