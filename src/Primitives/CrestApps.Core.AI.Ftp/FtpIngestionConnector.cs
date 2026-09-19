using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Reads files off an FTP or FTPS server.
/// </summary>
public sealed class FtpIngestionConnector : RemoteFileIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the file source's source.
    /// </summary>
    public const string ConnectorName = "Ftp";

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpIngestionConnector"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    public FtpIngestionConnector(FtpRemoteFileClientFactory clientFactory, ILogger<FtpIngestionConnector> logger)
        : base(clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override ValueTask ValidateAsync(IngestionSource settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = settings.GetOrCreate<FtpConnectionMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.Host))
        {
            result.Fail(new ValidationResult("A host is required.", [nameof(FtpConnectionMetadata.Host)]));
        }

        return ValueTask.CompletedTask;
    }
}
