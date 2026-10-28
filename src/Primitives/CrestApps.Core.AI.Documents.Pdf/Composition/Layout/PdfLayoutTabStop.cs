namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A tab stop: where text after a tab starts, ends or is centred.
/// </summary>
/// <param name="Position">The position, in points from the left edge of the paragraph's area, before its indent.</param>
/// <param name="Alignment">How the text after the tab sits against the position.</param>
/// <param name="DotLeader">Whether the gap the tab leaves is filled with dots, as in a table of contents.</param>
internal sealed record PdfLayoutTabStop(double Position, PdfLayoutAlignment Alignment = PdfLayoutAlignment.Left, bool DotLeader = false);
