using CrestApps.Core.AI.Documents.Presentations.Rendering;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Draws slides into a PDF, one page per slide, from the same display lists the slide preview paints.
/// </summary>
/// <remarks>
/// Registered by the PDF package, so a host that installs it can export decks as PDF; without it the
/// presentation tools offer PowerPoint files only.
/// </remarks>
public interface IPresentationPdfRenderer
{
    /// <summary>
    /// Renders slides as a PDF.
    /// </summary>
    /// <param name="slides">The slides, in order, as drawn by <see cref="SlideDrawingBuilder"/>.</param>
    /// <param name="title">The document title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The PDF.</returns>
    Task<byte[]> RenderAsync(IReadOnlyList<SlideDrawing> slides, string title, CancellationToken cancellationToken = default);
}
