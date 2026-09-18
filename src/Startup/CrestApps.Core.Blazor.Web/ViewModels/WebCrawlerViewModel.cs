using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class WebCrawlerViewModel
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; } = WebCrawlerConstants.Strategies.Sitemap;

    public string AIDataSourceId { get; set; }

    public bool Enabled { get; set; } = true;

    public int? ReindexIntervalMinutes { get; set; }

    public string SitemapBaseUrl { get; set; }

    public string SitemapUrl { get; set; }

    public int? SitemapMaxPages { get; set; }

    public int? SitemapMaxConcurrentRequests { get; set; }

    public int? SitemapRequestTimeoutSeconds { get; set; }

    public string SitemapUserAgent { get; set; }

    public string SitemapIncludeUrlPatterns { get; set; }

    public string SitemapExcludeUrlPatterns { get; set; }

    // Ingestion settings, which apply to every item this indexer reads.
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

    public static WebCrawlerViewModel FromCrawler(WebCrawler crawler)
    {
        ArgumentNullException.ThrowIfNull(crawler);

        var model = new WebCrawlerViewModel
        {
            ItemId = crawler.ItemId,
            DisplayText = crawler.DisplayText,
            Source = string.IsNullOrWhiteSpace(crawler.Source) ? WebCrawlerConstants.Strategies.Sitemap : FileSystemIngestionConnector.NormalizeSource(crawler.Source),
            AIDataSourceId = crawler.AIDataSourceId,
            Enabled = crawler.Enabled,
            ReindexIntervalMinutes = crawler.ReindexIntervalMinutes,
        };

        if (crawler.TryGet<SitemapWebCrawlerMetadata>(out var sitemap))
        {
            model.SitemapBaseUrl = sitemap.BaseUrl;
            model.SitemapUrl = sitemap.SitemapUrl;
            model.SitemapMaxPages = sitemap.MaxPages;
            model.SitemapMaxConcurrentRequests = sitemap.MaxConcurrentRequests;
            model.SitemapRequestTimeoutSeconds = sitemap.RequestTimeoutSeconds;
            model.SitemapUserAgent = sitemap.UserAgent;
            model.SitemapIncludeUrlPatterns = JoinPatterns(sitemap.IncludeUrlPatterns);
            model.SitemapExcludeUrlPatterns = JoinPatterns(sitemap.ExcludeUrlPatterns);
        }

        if (crawler.TryGet<IndexerMetadata>(out var indexer))
        {
            model.FigureMode = indexer.FigureMode;
            model.VisionDeploymentName = indexer.VisionDeploymentName;
            model.UtilityDeploymentName = indexer.UtilityDeploymentName;
            model.MaxFigureDescriptionsPerDocument = indexer.MaxFigureDescriptionsPerDocument;
            model.MaxItemsPerRun = indexer.MaxItemsPerRun;
            model.Language = indexer.Language;
        }

        if (crawler.TryGet<LocalFolderIndexerMetadata>(out var local))
        {
            model.LocalRootPath = local.RootPath;
            model.LocalSearchPattern = local.SearchPattern;
            model.LocalRecursive = local.Recursive;
        }

        if (crawler.TryGet<RemoteFolderIndexerMetadata>(out var remote))
        {
            model.RemoteRootPath = remote.RootPath;
            model.RemoteRecursive = remote.Recursive;
        }

        if (model.IsFtp && crawler.TryGet<FtpConnectionMetadata>(out var ftp))
        {
            model.RemoteHost = ftp.Host;
            model.RemotePort = ftp.Port;
            model.RemoteUsername = ftp.Username;
            model.RemoteHasPassword = !string.IsNullOrEmpty(ftp.Password);
            model.FtpEncryptionMode = ftp.EncryptionMode;
            model.FtpDataConnectionType = ftp.DataConnectionType;
            model.FtpValidateAnyCertificate = ftp.ValidateAnyCertificate;
        }

        if (model.IsSftp && crawler.TryGet<SftpConnectionMetadata>(out var sftp))
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
    /// <param name="crawler">The record to update.</param>
    /// <param name="dataProtectionProvider">
    /// Encrypts the file-server secrets with the same purpose the connectors decrypt them with.
    /// </param>
    public void ApplyTo(WebCrawler crawler, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(crawler);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        crawler.DisplayText = DisplayText?.Trim();
        crawler.Source = string.IsNullOrWhiteSpace(Source) ? WebCrawlerConstants.Strategies.Sitemap : Source.Trim();
        crawler.AIDataSourceId = AIDataSourceId?.Trim();
        crawler.Enabled = Enabled;
        crawler.ReindexIntervalMinutes = ReindexIntervalMinutes;

        crawler.Put(new IndexerMetadata
        {
            FigureMode = FigureMode,
            VisionDeploymentName = Trim(VisionDeploymentName),
            UtilityDeploymentName = Trim(UtilityDeploymentName),
            MaxFigureDescriptionsPerDocument = MaxFigureDescriptionsPerDocument,
            MaxItemsPerRun = MaxItemsPerRun,
            Language = Trim(Language),
        });

        crawler.Remove<SitemapWebCrawlerMetadata>();
        crawler.Remove<LocalFolderIndexerMetadata>();
        crawler.Remove<RemoteFolderIndexerMetadata>();

        if (IsFileSystem)
        {
            crawler.Put(new LocalFolderIndexerMetadata
            {
                RootPath = Trim(LocalRootPath),
                SearchPattern = string.IsNullOrWhiteSpace(LocalSearchPattern) ? "*.*" : LocalSearchPattern.Trim(),
                Recursive = LocalRecursive,
                MaxItems = MaxItemsPerRun,
            });
        }

        if (IsFtp || IsSftp)
        {
            crawler.Put(new RemoteFolderIndexerMetadata
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
            var ftp = crawler.GetOrCreate<FtpConnectionMetadata>();
            ftp.Host = Trim(RemoteHost);
            ftp.Port = RemotePort;
            ftp.Username = Trim(RemoteUsername);
            ftp.Password = ProtectOrReuse(RemotePassword, ftp.Password, protector);
            ftp.EncryptionMode = Trim(FtpEncryptionMode);
            ftp.DataConnectionType = Trim(FtpDataConnectionType);
            ftp.ValidateAnyCertificate = FtpValidateAnyCertificate;
            crawler.Put(ftp);
        }
        else
        {
            crawler.Remove<FtpConnectionMetadata>();
        }

        if (IsSftp)
        {
            var protector = dataProtectionProvider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);
            var sftp = crawler.GetOrCreate<SftpConnectionMetadata>();
            sftp.Host = Trim(RemoteHost);
            sftp.Port = RemotePort;
            sftp.Username = Trim(RemoteUsername);
            sftp.Password = ProtectOrReuse(RemotePassword, sftp.Password, protector);
            sftp.PrivateKey = ProtectOrReuse(SftpPrivateKey, sftp.PrivateKey, protector);
            sftp.Passphrase = ProtectOrReuse(SftpPassphrase, sftp.Passphrase, protector);
            crawler.Put(sftp);
        }
        else
        {
            crawler.Remove<SftpConnectionMetadata>();
        }

        if (string.Equals(crawler.Source, WebCrawlerConstants.Strategies.Sitemap, StringComparison.OrdinalIgnoreCase))
        {
            crawler.Put(new SitemapWebCrawlerMetadata
            {
                BaseUrl = SitemapBaseUrl?.Trim(),
                SitemapUrl = SitemapUrl?.Trim(),
                MaxPages = SitemapMaxPages,
                MaxConcurrentRequests = SitemapMaxConcurrentRequests,
                RequestTimeoutSeconds = SitemapRequestTimeoutSeconds,
                UserAgent = string.IsNullOrWhiteSpace(SitemapUserAgent) ? null : SitemapUserAgent.Trim(),
                IncludeUrlPatterns = SplitPatterns(SitemapIncludeUrlPatterns),
                ExcludeUrlPatterns = SplitPatterns(SitemapExcludeUrlPatterns),
            });
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

    private static string JoinPatterns(IEnumerable<string> patterns)
    {
        return patterns is null ? null : string.Join('\n', patterns);
    }

    private static List<string> SplitPatterns(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
