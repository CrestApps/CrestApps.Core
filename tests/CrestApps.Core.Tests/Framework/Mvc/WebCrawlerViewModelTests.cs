using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.WebCrawlers.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Covers what the indexer form does with a file server's connection settings.
/// </summary>
/// <remarks>
/// Two things here can only fail in production. A secret encrypted under a purpose the connector does not
/// decrypt with reads back as <see langword="null"/>, and the indexer then connects with no credential at
/// all; and a secret the form did not resend has to survive the save, or editing the port silently clears
/// the password. Both are asserted against the connectors' own purpose constants rather than a local copy.
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
        Assert.Equal(2121, metadata.Port);
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
    /// Verifies that the form is told a secret exists without being told what it is. Putting the stored
    /// value back on the model would re-encrypt ciphertext on the next save and leave the connector unable
    /// to read it.
    /// </summary>
    [Fact]
    public void FromCrawler_StoredSecrets_AreReportedButNeverExposed()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        var saved = CreateSftpModel(password: "pw", privateKey: "-----BEGIN KEY-----", passphrase: "phrase");
        saved.ApplyTo(crawler, provider);

        var reloaded = WebCrawlerViewModel.FromCrawler(crawler);

        Assert.True(reloaded.RemoteHasPassword);
        Assert.True(reloaded.SftpHasPrivateKey);
        Assert.True(reloaded.SftpHasPassphrase);
        Assert.Null(reloaded.RemotePassword);
        Assert.Null(reloaded.SftpPrivateKey);
        Assert.Null(reloaded.SftpPassphrase);
        Assert.Equal("files.example.com", reloaded.RemoteHost);
        Assert.Equal("reader", reloaded.RemoteUsername);
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

        CreateSftpModel(password: null, privateKey: "-----BEGIN KEY-----", passphrase: "phrase").ApplyTo(crawler, provider);

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
    /// Verifies that changing an indexer's protocol takes the previous protocol's connection with it, so a
    /// record never carries a credential for a server it no longer reads.
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
    public void ApplyTo_FileSystemSource_StoresNoConnection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        var model = new WebCrawlerViewModel
        {
            DisplayText = "Knowledge folder",
            Source = FileSystemIngestionConnector.ConnectorName,
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

    /// <summary>
    /// Verifies that a file-server indexer keeps its folder settings alongside its connection.
    /// </summary>
    [Fact]
    public void ApplyTo_FileServerSource_StoresTheRemoteFolder()
    {
        var provider = new EphemeralDataProtectionProvider();
        var crawler = new WebCrawler();

        var model = CreateFtpModel(password: "pw");
        model.RemoteRootPath = "/exports";
        model.RemoteRecursive = false;
        model.MaxItemsPerRun = 25;
        model.ApplyTo(crawler, provider);

        Assert.True(crawler.TryGet<RemoteFolderIndexerMetadata>(out var folder));
        Assert.Equal("/exports", folder.RootPath);
        Assert.False(folder.Recursive);
        Assert.Equal(25, folder.MaxItems);
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
