using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.Components.Pages.FileSources;

/// <summary>
/// Decides which records belong on the File Sources pages. A record's source is either a crawl strategy or
/// an ingestion connector, and that is the only thing separating the two kinds: a connector-backed record
/// reads files, while a strategy-backed record crawls a website and is managed on the Web Crawlers pages.
/// </summary>
public static class FileSourceFilter
{
    /// <summary>
    /// Determines whether a source names a registered ingestion connector.
    /// </summary>
    /// <param name="source">The record's source.</param>
    /// <param name="connectors">The registered ingestion connectors.</param>
    /// <returns><c>true</c> when the source is a registered ingestion connector; otherwise, <c>false</c>.</returns>
    public static bool IsIngestionConnector(string source, IReadOnlyList<IngestionConnectorDescriptor> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        // An unregistered source belongs to neither page, so it is left out rather than shown here by
        // default. Whatever registered it is gone, and the record cannot be run either way.
        foreach (var connector in connectors)
        {
            if (string.Equals(connector.Name, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps only the records the File Sources pages manage.
    /// </summary>
    /// <param name="fileSources">The records to filter.</param>
    /// <param name="connectors">The registered ingestion connectors.</param>
    /// <returns>The records whose source is a registered ingestion connector.</returns>
    public static List<WebCrawler> SelectFileSourceRecords(IEnumerable<WebCrawler> fileSources, IReadOnlyList<IngestionConnectorDescriptor> connectors)
    {
        ArgumentNullException.ThrowIfNull(fileSources);
        ArgumentNullException.ThrowIfNull(connectors);

        return fileSources
            .Where(fileSource => IsIngestionConnector(fileSource.Source, connectors))
            .ToList();
    }
}
