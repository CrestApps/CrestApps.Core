namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// What a document says about itself in its own front matter.
/// </summary>
public sealed class PublicationMetadata
{
    /// <summary>
    /// Gets or sets the title of the publication as a whole.
    /// </summary>
    public string PublicationTitle { get; set; }

    /// <summary>
    /// Gets or sets the publisher or issuing body.
    /// </summary>
    public string Publisher { get; set; }

    /// <summary>
    /// Gets or sets the responsible editor.
    /// </summary>
    public string Editor { get; set; }

    /// <summary>
    /// Gets or sets the place of publication.
    /// </summary>
    public string Place { get; set; }

    /// <summary>
    /// Gets or sets the volume, exactly as printed.
    /// </summary>
    public string Volume { get; set; }

    /// <summary>
    /// Gets or sets the issue or part number, exactly as printed.
    /// </summary>
    public string Issue { get; set; }

    /// <summary>
    /// Gets or sets the date or period the issue covers, exactly as printed.
    /// </summary>
    public string Date { get; set; }

    /// <summary>
    /// Gets or sets the ISSN or ISBN, when one is printed.
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// Gets a value indicating whether anything at all was found.
    /// </summary>
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(PublicationTitle) ||
        !string.IsNullOrWhiteSpace(Publisher) ||
        !string.IsNullOrWhiteSpace(Issue) ||
        !string.IsNullOrWhiteSpace(Date);
}
