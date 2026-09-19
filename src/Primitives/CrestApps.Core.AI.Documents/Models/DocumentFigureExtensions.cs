using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Models;

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
