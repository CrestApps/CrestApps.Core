namespace CrestApps.Core.AI.Models;

/// <summary>
/// What the document an object came from said about itself in its own front matter, so the issue it was
/// printed in can be searched for and named.
/// </summary>
/// <remarks>
/// A data source holding a year of one publication holds twelve documents that differ only by issue, and the
/// file names tell them apart only as well as whoever named the files did. Carrying the masthead onto the
/// objects is what lets a search be narrowed to one issue, and a citation name it.
/// <para>
/// Every object built from a document carries this, rather than the document object carrying it for all of
/// them. An object is indexed on its own — an incremental sync of one figure fetches that figure and nothing
/// else — so an object that could not answer for itself would be indexed without an issue while a full
/// re-index gave it one.
/// </para>
/// </remarks>
public sealed class PublicationDetails
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
}
