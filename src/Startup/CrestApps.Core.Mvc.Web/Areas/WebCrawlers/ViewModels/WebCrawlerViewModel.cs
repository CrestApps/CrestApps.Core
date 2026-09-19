using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.WebCrawlers.ViewModels;

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

    // Ingestion settings, which apply to every item this source reads. Figure transcription is the
    // expensive part, and how much of it is worth paying for depends entirely on the corpus.
    public FigureProcessingMode FigureMode { get; set; } = FigureProcessingMode.Auto;

    public string VisionDeploymentName { get; set; }

    public string UtilityDeploymentName { get; set; }

    public int MaxFigureDescriptionsPerDocument { get; set; } = 25;

    public int? MaxItemsPerRun { get; set; }

    public string Language { get; set; }

    [BindNever]
    public FileSourceRunSummary LastRun { get; set; }

    [BindNever]
    public IEnumerable<SelectListItem> Strategies { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> VisionDeployments { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> UtilityDeployments { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> DataSources { get; set; } = [];

    public static WebCrawlerViewModel FromCrawler(WebCrawler crawler)
    {
        var model = new WebCrawlerViewModel
        {
            ItemId = crawler.ItemId,
            DisplayText = crawler.DisplayText,
            Source = string.IsNullOrWhiteSpace(crawler.Source) ? WebCrawlerConstants.Strategies.Sitemap : crawler.Source,
            AIDataSourceId = crawler.AIDataSourceId,
            Enabled = crawler.Enabled,
            ReindexIntervalMinutes = crawler.ReindexIntervalMinutes,
        };

        if (crawler.TryGet<FileSourceMetadata>(out var source))
        {
            model.FigureMode = source.FigureMode;
            model.VisionDeploymentName = source.VisionDeploymentName;
            model.UtilityDeploymentName = source.UtilityDeploymentName;
            model.MaxFigureDescriptionsPerDocument = source.MaxFigureDescriptionsPerDocument;
            model.MaxItemsPerRun = source.MaxItemsPerRun;
            model.Language = source.Language;
        }

        if (crawler.TryGet<FileSourceRunSummary>(out var lastRun))
        {
            model.LastRun = lastRun;
        }

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

        return model;
    }

    /// <summary>
    /// Writes the form onto the record.
    /// </summary>
    /// <param name="crawler">The record to update.</param>
    public void ApplyTo(WebCrawler crawler)
    {
        ArgumentNullException.ThrowIfNull(crawler);

        crawler.DisplayText = DisplayText?.Trim();
        crawler.Source = string.IsNullOrWhiteSpace(Source) ? WebCrawlerConstants.Strategies.Sitemap : Source.Trim();
        crawler.AIDataSourceId = AIDataSourceId?.Trim();
        crawler.Enabled = Enabled;
        crawler.ReindexIntervalMinutes = ReindexIntervalMinutes;

        crawler.Put(new FileSourceMetadata
        {
            FigureMode = FigureMode,
            VisionDeploymentName = Trim(VisionDeploymentName),
            UtilityDeploymentName = Trim(UtilityDeploymentName),
            MaxFigureDescriptionsPerDocument = MaxFigureDescriptionsPerDocument,
            MaxItemsPerRun = MaxItemsPerRun,
            Language = Trim(Language),
        });

        crawler.Remove<SitemapWebCrawlerMetadata>();

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
