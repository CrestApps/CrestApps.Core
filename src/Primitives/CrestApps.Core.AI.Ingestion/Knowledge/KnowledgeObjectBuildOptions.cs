using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// What the builder needs to know about the file it is turning into knowledge objects.
/// </summary>
public sealed class KnowledgeObjectBuildOptions
{
    /// <summary>
    /// Gets or sets the short, stable key derived from the file's bytes. Identical bytes produce identical
    /// keys, so re-ingesting the same file replaces its objects instead of duplicating them.
    /// </summary>
    public string FileKey { get; set; }

    /// <summary>
    /// Gets or sets the data source the objects belong to.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the document title used for citation.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the BCP-47 language tag of the document, when it is known.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the indexer that produced the file, or <see langword="null"/> for a manual upload.
    /// </summary>
    public string IndexerId { get; set; }

    /// <summary>
    /// Gets or sets the identifier the connector knows the source item by, or <see langword="null"/> for a
    /// manual upload.
    /// </summary>
    public string SourceItemId { get; set; }

    /// <summary>
    /// Gets or sets the hash of the whole file.
    /// </summary>
    public string ContentHash { get; set; }

    /// <summary>
    /// Gets or sets what the document turned out to be made of, or <see langword="null"/> when nothing was
    /// analyzed and the whole file is one article.
    /// </summary>
    public Structure.DocumentStructure Structure { get; set; }

    /// <summary>
    /// Gets or sets the words in a caption or a description that say a figure is a chart, or
    /// <see langword="null"/> to use <see cref="KnowledgeObjectBuilder.DefaultChartKeywords"/>.
    /// </summary>
    public IReadOnlyList<string> ChartKeywords { get; set; }

    /// <summary>
    /// Gets or sets what the document says about the issue it was printed in, or <see langword="null"/> when
    /// its front matter said nothing. It names the objects and is carried onto every one of them.
    /// </summary>
    public PublicationDetails Publication { get; set; }
}
