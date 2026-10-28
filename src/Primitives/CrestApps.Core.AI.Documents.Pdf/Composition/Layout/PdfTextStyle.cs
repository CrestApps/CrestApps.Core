using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// How a piece of text is drawn, once its font is resolved.
/// </summary>
/// <param name="Font">The font.</param>
/// <param name="Size">The size, in points.</param>
/// <param name="Color">The colour.</param>
/// <param name="Underline">Whether it is underlined.</param>
internal readonly record struct PdfTextStyle(PdfFont Font, double Size, PdfColor Color, bool Underline);
