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
/// Covers the Blazor indexer form's handling of a file server's connection settings.
/// </summary>
/// <remarks>
/// The two hosts write to the same record and the same connectors read it, so the rules cannot differ
/// between them: the same purpose constants, the same write-only secrets, and the same removal of the
/// protocol that is no longer selected. These assertions deliberately mirror the MVC ones.
/// </remarks>
public sealed class WebCrawlerViewModelTests
{
    /// <summary>
    /// Verifies that an FTP password is stored encrypted under the purpose the FTP client factory reads it
    /// back with.
    /// </summary>
    [Fact]
    public void ApplyTo_FtpPassword_IsEncryptedUnderTheConnectorsPurpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        CreateFtpModel(password: "correct horse").ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<FtpConnectionMetadata>(out var metadata));
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
        var crawler = new WebCrawler();

        CreateFtpModel(password: "original").ApplyTo(crawler, provider);

        var model = CreateFtpModel(password: null);
        model.RemotePort = 990;
        model.ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<FtpConnectionMetadata>(out var metadata));
        Assert.Equal(990, metadata.Port);

        var connectorProtector = provider.CreateProtector(FtpResourceConstants.DataProtectionPurpose);

        Assert.Equal("original", connectorProtector.Unprotect(metadata.Password));
    }

    /// <summary>
    /// Verifies that the form is told a secret exists without being told what it is.
    /// </summary>
    [Fact]
    public void FromCrawler_StoredSecrets_AreReportedButNeverExposed()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        CreateSftpModel(password: "pw", privateKey: "-----BEGIN KEY-----", passphrase: "phrase")
            .ApplyTo(crawler, provider);

        var reloaded = WebCrawlerViewModel.FromCrawler(crawler);

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
        var crawler = new WebCrawler();

        CreateSftpModel(password: null, privateKey: "-----BEGIN KEY-----", passphrase: "phrase")
            .ApplyTo(crawler, provider);

        var connectorProtector = provider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);

        Assert.True(crawler.TryGet<SftpConnectionMetadata>(out var stored));
        Assert.Equal("-----BEGIN KEY-----", connectorProtector.Unprotect(stored.PrivateKey));
        Assert.Equal("phrase", connectorProtector.Unprotect(stored.Passphrase));

        CreateSftpModel(password: null, privateKey: null, passphrase: null).ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<SftpConnectionMetadata>(out var reloaded));
        Assert.Equal("-----BEGIN KEY-----", connectorProtector.Unprotect(reloaded.PrivateKey));
        Assert.Equal("phrase", connectorProtector.Unprotect(reloaded.Passphrase));
    }

    /// <summary>
    /// Verifies that changing an indexer's protocol takes the previous protocol's connection with it.
    /// </summary>
    [Fact]
    public void ApplyTo_SwitchingProtocol_DropsTheOtherConnection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        CreateFtpModel(password: "ftp-secret").ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<FtpConnectionMetadata>(out _));

        CreateSftpModel(password: "sftp-secret", privateKey: null, passphrase: null).ApplyTo(crawler, provider);

        Assert.False(crawler.TryGet<FtpConnectionMetadata>(out _));
        Assert.True(crawler.TryGet<SftpConnectionMetadata>(out _));
    }

    /// <summary>
    /// Verifies that a folder indexer stores folder settings and no connection at all.
    /// </summary>
    [Fact]
    public void ApplyTo_LocalFolderSource_StoresNoConnection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        var model = new WebCrawlerViewModel
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

        model.ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<LocalFolderIndexerMetadata>(out var folder));
        Assert.Equal("D:\\knowledge", folder.RootPath);
        Assert.Equal("*.pdf", folder.SearchPattern);
        Assert.False(folder.Recursive);

        Assert.False(crawler.TryGet<FtpConnectionMetadata>(out _));
        Assert.False(crawler.TryGet<SftpConnectionMetadata>(out _));
        Assert.False(crawler.TryGet<RemoteFolderIndexerMetadata>(out _));
    }

    private static WebCrawlerViewModel CreateFtpModel(string password)
    {
        return new WebCrawlerViewModel
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

    private static WebCrawlerViewModel CreateSftpModel(string password, string privateKey, string passphrase)
    {
        return new WebCrawlerViewModel
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
