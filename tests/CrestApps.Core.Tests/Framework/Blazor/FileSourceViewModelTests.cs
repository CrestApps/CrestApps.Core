using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Tests.Framework.Blazor;

/// <summary>
/// Covers the Blazor File Sources form's handling of a file server's connection settings.
/// </summary>
/// <remarks>
/// The two hosts write to the same record and the same connectors read it, so the rules cannot differ
/// between them: the same purpose constants, the same write-only secrets, and the same removal of the
/// protocol that is no longer selected. These assertions deliberately mirror the MVC ones.
/// </remarks>
public sealed class FileSourceViewModelTests
{
    /// <summary>
    /// Verifies that an FTP password is stored encrypted under the purpose the FTP client factory reads it
    /// back with.
    /// </summary>
    [Fact]
    public void ApplyTo_FtpPassword_IsEncryptedUnderTheConnectorsPurpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        CreateFtpModel(password: "correct horse").ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<FtpConnectionMetadata>(out var metadata));
        Assert.Equal("files.example.com", metadata.Host);
        Assert.Equal("reader", metadata.Username);
        Assert.NotEqual("correct horse", metadata.Password);

        var connectorProtector = provider.CreateProtector(FtpResourceConstants.DataProtectionPurpose);

        Assert.Equal("correct horse", connectorProtector.Unprotect(metadata.Password));
    }

    /// <summary>
    /// Verifies that saving the form again without retyping the password keeps the stored one.
    /// </summary>
    [Fact]
    public void ApplyTo_BlankFtpPassword_KeepsTheStoredSecret()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        CreateFtpModel(password: "original").ApplyTo(fileSource, provider);

        var model = CreateFtpModel(password: null);
        model.RemotePort = 990;
        model.ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<FtpConnectionMetadata>(out var metadata));
        Assert.Equal(990, metadata.Port);

        var connectorProtector = provider.CreateProtector(FtpResourceConstants.DataProtectionPurpose);

        Assert.Equal("original", connectorProtector.Unprotect(metadata.Password));
    }

    /// <summary>
    /// Verifies that the form is told a secret exists without being told what it is.
    /// </summary>
    [Fact]
    public void FromFileSource_StoredSecrets_AreReportedButNeverExposed()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        CreateSftpModel(password: "pw", privateKey: "-----BEGIN KEY-----", passphrase: "phrase")
            .ApplyTo(fileSource, provider);

        var reloaded = FileSourceViewModel.FromFileSource(fileSource);

        Assert.True(reloaded.RemoteHasPassword);
        Assert.True(reloaded.SftpHasPrivateKey);
        Assert.True(reloaded.SftpHasPassphrase);
        Assert.Null(reloaded.RemotePassword);
        Assert.Null(reloaded.SftpPrivateKey);
        Assert.Null(reloaded.SftpPassphrase);
    }

    /// <summary>
    /// Verifies that the SFTP key material is encrypted under the purpose the SFTP client factory uses, and
    /// that leaving either field blank keeps what is stored.
    /// </summary>
    [Fact]
    public void ApplyTo_SftpKeyMaterial_IsEncryptedAndReusedWhenBlank()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        CreateSftpModel(password: null, privateKey: "-----BEGIN KEY-----", passphrase: "phrase")
            .ApplyTo(fileSource, provider);

        var connectorProtector = provider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);

        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var stored));
        Assert.Equal("-----BEGIN KEY-----", connectorProtector.Unprotect(stored.PrivateKey));
        Assert.Equal("phrase", connectorProtector.Unprotect(stored.Passphrase));

        CreateSftpModel(password: null, privateKey: null, passphrase: null).ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var reloaded));
        Assert.Equal("-----BEGIN KEY-----", connectorProtector.Unprotect(reloaded.PrivateKey));
        Assert.Equal("phrase", connectorProtector.Unprotect(reloaded.Passphrase));
    }

    /// <summary>
    /// Verifies that changing a file source's protocol takes the previous protocol's connection with it.
    /// </summary>
    [Fact]
    public void ApplyTo_SwitchingProtocol_DropsTheOtherConnection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        CreateFtpModel(password: "ftp-secret").ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<FtpConnectionMetadata>(out _));

        CreateSftpModel(password: "sftp-secret", privateKey: null, passphrase: null).ApplyTo(fileSource, provider);

        Assert.False(fileSource.TryGet<FtpConnectionMetadata>(out _));
        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out _));
    }

    /// <summary>
    /// Verifies that a folder file source stores folder settings and no connection at all.
    /// </summary>
    [Fact]
    public void ApplyTo_LocalFolderSource_StoresNoConnection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        var model = new FileSourceViewModel
        {
            DisplayText = "Knowledge folder",
            Source = LocalFolderIngestionConnector.ConnectorName,
            AIDataSourceId = "data-source-1",
            LocalRootPath = "D:\\knowledge",
            LocalSearchPattern = "*.pdf",
            LocalRecursive = false,
            RemoteHost = "left over from another source",
            RemotePassword = "left over too",
        };

        model.ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<LocalFolderIndexerMetadata>(out var folder));
        Assert.Equal("D:\\knowledge", folder.RootPath);
        Assert.Equal("*.pdf", folder.SearchPattern);
        Assert.False(folder.Recursive);

        Assert.False(fileSource.TryGet<FtpConnectionMetadata>(out _));
        Assert.False(fileSource.TryGet<SftpConnectionMetadata>(out _));
        Assert.False(fileSource.TryGet<RemoteFolderIndexerMetadata>(out _));
    }

    /// <summary>
    /// Verifies that the form's run interval is the interval the background service reads, which the record
    /// still names for the crawler it was written for.
    /// </summary>
    [Fact]
    public void ApplyTo_RunInterval_RoundTripsThroughTheRecordsInterval()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        var model = CreateFtpModel(password: "pw");
        model.RunIntervalMinutes = 90;
        model.ApplyTo(fileSource, provider);

        Assert.Equal(90, fileSource.ReindexIntervalMinutes);
        Assert.Equal(90, FileSourceViewModel.FromFileSource(fileSource).RunIntervalMinutes);
    }

    /// <summary>
    /// Verifies that the ingestion settings every connector shares are written to the record the run
    /// service reads them from.
    /// </summary>
    [Fact]
    public void ApplyTo_IngestionSettings_AreStoredOnTheRecord()
    {
        var provider = new EphemeralDataProtectionProvider();
        var fileSource = new WebCrawler();

        var model = CreateFtpModel(password: "pw");
        model.MaxItemsPerRun = 25;
        model.Language = "en";
        model.ApplyTo(fileSource, provider);

        Assert.True(fileSource.TryGet<IndexerMetadata>(out var indexer));
        Assert.Equal(25, indexer.MaxItemsPerRun);
        Assert.Equal("en", indexer.Language);

        // The remote folder carries its own copy, because the connector lists a page at a time.
        Assert.True(fileSource.TryGet<RemoteFolderIndexerMetadata>(out var remote));
        Assert.Equal(25, remote.MaxItems);
    }

    private static FileSourceViewModel CreateFtpModel(string password)
    {
        return new FileSourceViewModel
        {
            DisplayText = "Reports drop",
            Source = FtpIngestionConnector.ConnectorName,
            AIDataSourceId = "data-source-1",
            RemoteHost = "files.example.com",
            RemotePort = 2121,
            RemoteUsername = "reader",
            RemotePassword = password,
            FtpEncryptionMode = "Explicit",
            FtpDataConnectionType = "AutoPassive",
        };
    }

    private static FileSourceViewModel CreateSftpModel(string password, string privateKey, string passphrase)
    {
        return new FileSourceViewModel
        {
            DisplayText = "Reports drop",
            Source = SftpIngestionConnector.ConnectorName,
            AIDataSourceId = "data-source-1",
            RemoteHost = "files.example.com",
            RemotePort = 2222,
            RemoteUsername = "reader",
            RemotePassword = password,
            SftpPrivateKey = privateKey,
            SftpPassphrase = passphrase,
        };
    }
}
