using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents one configured file source: somewhere files are read from, identified by the ingestion
/// connector named in its <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> (for example
/// <c>FileSystem</c>, <c>Ftp</c> or <c>Sftp</c>), the connector's settings (stored in
/// <see cref="CrestApps.Core.ExtensibleEntity.Properties"/>), and the target
/// <see cref="IngestionSource.AIDataSourceId"/> whose knowledge base the files are ingested into.
/// </summary>
/// <remarks>
/// A file source is its own record in its own store. It was once stored as a <see cref="WebCrawler"/> whose
/// source happened to name a connector rather than a crawl strategy, which meant an FTP connection and a
/// website shared a table, a set of handlers and a screen filter, and the only thing telling them apart was
/// a string lookup that each screen did differently.
/// </remarks>
public sealed class FileSource : IngestionSource, ICloneable<FileSource>
{
    /// <summary>
    /// Clones the file source.
    /// </summary>
    public FileSource Clone()
    {
        var clone = new FileSource();

        CopyTo(clone);

        return clone;
    }
}
