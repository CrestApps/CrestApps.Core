using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// The File Sources form: where the files come from, which data source receives them, and how they are read.
/// </summary>
/// <remarks>
/// A file source is stored as a <see cref="WebCrawler"/> record whose source is an ingestion connector
/// rather than a crawl strategy. The record type is what it is for history's sake; nothing on this form is
/// about crawling a website.
/// </remarks>
public sealed class FileSourceViewModel
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; }

    public string AIDataSourceId { get; set; }

    public bool Enabled { get; set; } = true;

    // Stored as the record's re-index interval, which is what the background service reads.
    public int? RunIntervalMinutes { get; set; }

    // Ingestion settings, which apply to every item this file source reads.
    public FigureProcessingMode FigureMode { get; set; } = FigureProcessingMode.Auto;

    public string VisionDeploymentName { get; set; }

    public string UtilityDeploymentName { get; set; }


    public int MaxFigureDescriptionsPerDocument { get; set; } = 25;

    public int? MaxItemsPerRun { get; set; }

    public string Language { get; set; }

    // File-system folder settings.
    public string LocalRootPath { get; set; }

    public string LocalSearchPattern { get; set; } = "*.*";

    public bool LocalRecursive { get; set; } = true;

    // File-server folder settings, shared by FTP and SFTP.
    public string RemoteRootPath { get; set; } = "/";

    public bool RemoteRecursive { get; set; } = true;

    // File-server connection settings, shared by FTP and SFTP. A secret is write-only: the form learns that
    // one is stored, never what it is, and a field left blank keeps what is stored.
    public string RemoteHost { get; set; }

    public int? RemotePort { get; set; }

    public string RemoteUsername { get; set; }

    public string RemotePassword { get; set; }

    public bool RemoteHasPassword { get; set; }

    // FTP only.
    public string FtpEncryptionMode { get; set; }

    public string FtpDataConnectionType { get; set; }

    public bool FtpValidateAnyCertificate { get; set; }

    // SFTP only.
    public string SftpPrivateKey { get; set; }

    public bool SftpHasPrivateKey { get; set; }

    public string SftpPassphrase { get; set; }

    public bool SftpHasPassphrase { get; set; }

    public bool IsFileSystem
        => string.Equals(Source, FileSystemIngestionConnector.ConnectorName, StringComparison.OrdinalIgnoreCase);

    public bool IsFtp
        => string.Equals(Source, FtpIngestionConnector.ConnectorName, StringComparison.OrdinalIgnoreCase);

    public bool IsSftp
        => string.Equals(Source, SftpIngestionConnector.ConnectorName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the form off the record.
    /// </summary>
    /// <param name="fileSource">The record to read.</param>
    /// <returns>The populated form.</returns>
    public static FileSourceViewModel FromFileSource(WebCrawler fileSource)
    {
        ArgumentNullException.ThrowIfNull(fileSource);

        var model = new FileSourceViewModel
        {
            ItemId = fileSource.ItemId,
            DisplayText = fileSource.DisplayText,
            Source = fileSource.Source,
            AIDataSourceId = fileSource.AIDataSourceId,
            Enabled = fileSource.Enabled,
            RunIntervalMinutes = fileSource.ReindexIntervalMinutes,
        };

        if (fileSource.TryGet<IndexerMetadata>(out var indexer))
        {
            model.FigureMode = indexer.FigureMode;
            model.VisionDeploymentName = indexer.VisionDeploymentName;
            model.UtilityDeploymentName = indexer.UtilityDeploymentName;
            model.MaxFigureDescriptionsPerDocument = indexer.MaxFigureDescriptionsPerDocument;
            model.MaxItemsPerRun = indexer.MaxItemsPerRun;
            model.Language = indexer.Language;
        }

        if (fileSource.TryGet<LocalFolderIndexerMetadata>(out var local))
        {
            model.LocalRootPath = local.RootPath;
            model.LocalSearchPattern = local.SearchPattern;
            model.LocalRecursive = local.Recursive;
        }

        if (fileSource.TryGet<RemoteFolderIndexerMetadata>(out var remote))
        {
            model.RemoteRootPath = remote.RootPath;
            model.RemoteRecursive = remote.Recursive;
        }

        if (model.IsFtp && fileSource.TryGet<FtpConnectionMetadata>(out var ftp))
        {
            model.RemoteHost = ftp.Host;
            model.RemotePort = ftp.Port;
            model.RemoteUsername = ftp.Username;
            model.RemoteHasPassword = !string.IsNullOrEmpty(ftp.Password);
            model.FtpEncryptionMode = ftp.EncryptionMode;
            model.FtpDataConnectionType = ftp.DataConnectionType;
            model.FtpValidateAnyCertificate = ftp.ValidateAnyCertificate;
        }

        if (model.IsSftp && fileSource.TryGet<SftpConnectionMetadata>(out var sftp))
        {
            model.RemoteHost = sftp.Host;
            model.RemotePort = sftp.Port;
            model.RemoteUsername = sftp.Username;
            model.RemoteHasPassword = !string.IsNullOrEmpty(sftp.Password);
            model.SftpHasPrivateKey = !string.IsNullOrEmpty(sftp.PrivateKey);
            model.SftpHasPassphrase = !string.IsNullOrEmpty(sftp.Passphrase);
        }

        return model;
    }

    /// <summary>
    /// Writes the form onto the record.
    /// </summary>
    /// <param name="fileSource">The record to update.</param>
    /// <param name="dataProtectionProvider">
    /// Encrypts the file-server secrets with the same purpose the connectors decrypt them with.
    /// </param>
    public void ApplyTo(WebCrawler fileSource, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(fileSource);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        fileSource.DisplayText = DisplayText?.Trim();
        fileSource.Source = Source?.Trim();
        fileSource.AIDataSourceId = AIDataSourceId?.Trim();
        fileSource.Enabled = Enabled;
        fileSource.ReindexIntervalMinutes = RunIntervalMinutes;

        fileSource.Put(new IndexerMetadata
        {
            FigureMode = FigureMode,
            VisionDeploymentName = Trim(VisionDeploymentName),
            UtilityDeploymentName = Trim(UtilityDeploymentName),
            MaxFigureDescriptionsPerDocument = MaxFigureDescriptionsPerDocument,
            MaxItemsPerRun = MaxItemsPerRun,
            Language = Trim(Language),
        });

        fileSource.Remove<LocalFolderIndexerMetadata>();
        fileSource.Remove<RemoteFolderIndexerMetadata>();

        if (IsFileSystem)
        {
            fileSource.Put(new LocalFolderIndexerMetadata
            {
                RootPath = Trim(LocalRootPath),
                SearchPattern = string.IsNullOrWhiteSpace(LocalSearchPattern) ? "*.*" : LocalSearchPattern.Trim(),
                Recursive = LocalRecursive,
                MaxItems = MaxItemsPerRun,
            });
        }

        if (IsFtp || IsSftp)
        {
            fileSource.Put(new RemoteFolderIndexerMetadata
            {
                RootPath = string.IsNullOrWhiteSpace(RemoteRootPath) ? "/" : RemoteRootPath.Trim(),
                Recursive = RemoteRecursive,
                MaxItems = MaxItemsPerRun,
            });
        }

        // The connection is written over what is stored rather than replaced, so a secret the form did not
        // resend survives the save. Settings for the protocol that is no longer selected are dropped.
        if (IsFtp)
        {
            var protector = dataProtectionProvider.CreateProtector(FtpResourceConstants.DataProtectionPurpose);
            var ftp = fileSource.GetOrCreate<FtpConnectionMetadata>();
            ftp.Host = Trim(RemoteHost);
            ftp.Port = RemotePort;
            ftp.Username = Trim(RemoteUsername);
            ftp.Password = ProtectOrReuse(RemotePassword, ftp.Password, protector);
            ftp.EncryptionMode = Trim(FtpEncryptionMode);
            ftp.DataConnectionType = Trim(FtpDataConnectionType);
            ftp.ValidateAnyCertificate = FtpValidateAnyCertificate;
            fileSource.Put(ftp);
        }
        else
        {
            fileSource.Remove<FtpConnectionMetadata>();
        }

        if (IsSftp)
        {
            var protector = dataProtectionProvider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);
            var sftp = fileSource.GetOrCreate<SftpConnectionMetadata>();
            sftp.Host = Trim(RemoteHost);
            sftp.Port = RemotePort;
            sftp.Username = Trim(RemoteUsername);
            sftp.Password = ProtectOrReuse(RemotePassword, sftp.Password, protector);
            sftp.PrivateKey = ProtectOrReuse(SftpPrivateKey, sftp.PrivateKey, protector);
            sftp.Passphrase = ProtectOrReuse(SftpPassphrase, sftp.Passphrase, protector);
            fileSource.Put(sftp);
        }
        else
        {
            fileSource.Remove<SftpConnectionMetadata>();
        }
    }

    private static string ProtectOrReuse(string newValue, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(newValue) ? existingValue : protector.Protect(newValue);
    }

    private static string Trim(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
