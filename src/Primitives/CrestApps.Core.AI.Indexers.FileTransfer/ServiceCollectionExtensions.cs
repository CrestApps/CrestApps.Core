using CrestApps.Core.AI.Indexers.FileTransfer.Ftp;
using CrestApps.Core.AI.Indexers.FileTransfer.Sftp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Indexers.FileTransfer;

/// <summary>
/// Registers the file-server ingestion connectors.
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
}
