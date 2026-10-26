namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// One character as a font writes it.
/// </summary>
/// <param name="Code">The code written: a glyph for a TrueType font, a byte for a standard one.</param>
/// <param name="Advance">How far it moves the pen, in ems.</param>
/// <param name="Text">The text it stands for.</param>
internal readonly record struct PdfFontCode(int Code, double Advance, string Text);
