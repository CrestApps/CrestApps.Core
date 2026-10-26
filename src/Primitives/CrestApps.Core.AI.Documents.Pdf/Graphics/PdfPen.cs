using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics;

/// <summary>
/// How a line is drawn.
/// </summary>
/// <param name="Color">The colour.</param>
/// <param name="Width">The width, in points.</param>
/// <param name="Opacity">The opacity, from 0 (invisible) to 1.</param>
/// <param name="Dash">The lengths of the dashes and gaps, in points, or <see langword="null"/> for a solid line.</param>
/// <param name="RoundJoins">Whether corners are rounded rather than mitred.</param>
internal sealed record PdfPen(PdfColor Color, double Width, double Opacity = 1, IReadOnlyList<double> Dash = null, bool RoundJoins = false);
