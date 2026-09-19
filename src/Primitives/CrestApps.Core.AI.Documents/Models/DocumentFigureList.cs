namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// The figures recovered from one uploaded document, carried on the document itself so a later tool can show
/// them without re-reading the file.
/// </summary>
public sealed class DocumentFigureList
{
    /// <summary>
    /// Gets or sets the figures, in the order they were read.
    /// </summary>
    public IList<DocumentFigure> Figures { get; set; } = [];
}
