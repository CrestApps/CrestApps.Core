using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// One page, measured: what is on it and what its ordinary body text looks like. Caption detection compares
/// against the page's own typography, because "small type" only means anything relative to the page it sits on.
/// </summary>
public sealed class FigureCaptionPageContext
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Gets the figures on the page.
    /// </summary>
    public IReadOnlyList<IngestionDocumentImage> Images { get; init; } = [];

    /// <summary>
    /// Gets the text elements on the page.
    /// </summary>
    public IReadOnlyList<IngestionDocumentElement> Paragraphs { get; init; } = [];

    /// <summary>
    /// Gets the font size most of the page's text is set in.
    /// </summary>
    public double ModalPointSize { get; init; }

    /// <summary>
    /// Gets the font most of the page's text is set in.
    /// </summary>
    public string ModalFontName { get; init; }

    /// <summary>
    /// Gets the page's typical line height, which distances are measured in so a gap means the same thing on
    /// a densely set page as on an airy one.
    /// </summary>
    public double ModalLineHeight { get; init; }
}
