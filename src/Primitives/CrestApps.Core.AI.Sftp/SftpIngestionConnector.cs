using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Reads files off an SFTP server.
/// </summary>
public sealed class SftpIngestionConnector : RemoteFileIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the file source's source.
    /// </summary>
    public const string ConnectorName = "Sftp";

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpIngestionConnector"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    public SftpIngestionConnector(SftpRemoteFileClientFactory clientFactory, ILogger<SftpIngestionConnector> logger)
        : base(clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override ValueTask ValidateAsync(IngestionSource settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = settings.GetOrCreate<SftpConnectionMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.Host))
        {
            result.Fail(new ValidationResult("A host is required.", [nameof(SftpConnectionMetadata.Host)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.Username))
        {
            result.Fail(new ValidationResult("A user name is required.", [nameof(SftpConnectionMetadata.Username)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.Password) && string.IsNullOrWhiteSpace(metadata.PrivateKey))
        {
            result.Fail(new ValidationResult(
                "Either a password or a private key is required.",
                [nameof(SftpConnectionMetadata.Password)]));
        }

        return ValueTask.CompletedTask;
    }
}
