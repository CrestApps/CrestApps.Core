using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// One figure recovered from an uploaded document, and where its bytes were stored.
/// </summary>
public sealed class DocumentFigure
{
    /// <summary>
    /// Gets or sets the figure identifier, which is stable across repeated reads of the same file.
    /// </summary>
    public string FigureId { get; set; }

    /// <summary>
    /// Gets or sets the page the figure was printed on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the caption printed with the figure, when it had one.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets the relative path the figure bytes were stored at.
    /// </summary>
    public string StoragePath { get; set; }

    /// <summary>
    /// Gets or sets the media type of the stored bytes.
    /// </summary>
    public string MediaType { get; set; }
}

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

/// <summary>
/// Extension methods for reading the figures recorded on an <see cref="AIDocument"/>.
/// </summary>
public static class DocumentFigureExtensions
{
    /// <summary>
    /// Gets the figures recorded on the document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The figures, or an empty list when none were recorded.</returns>
    public static IReadOnlyList<DocumentFigure> GetFigures(this AIDocument document)
    {
        if (document is null || !document.TryGet<DocumentFigureList>(out var list) || list.Figures is null)
        {
            return [];
        }

        return list.Figures.ToArray();
    }

    /// <summary>
    /// Finds one figure recorded on the document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="figureId">The figure identifier.</param>
    /// <returns>The figure, or <see langword="null"/> when the document lists no such figure.</returns>
    public static DocumentFigure FindFigure(this AIDocument document, string figureId)
    {
        if (string.IsNullOrWhiteSpace(figureId))
        {
            return null;
        }

        return document.GetFigures()
            .FirstOrDefault(figure => string.Equals(figure.FigureId, figureId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
