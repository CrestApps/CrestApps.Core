using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Indexers.FileTransfer.Sftp;

/// <summary>
/// Registers the SFTP ingestion connector.
/// </summary>
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
}
