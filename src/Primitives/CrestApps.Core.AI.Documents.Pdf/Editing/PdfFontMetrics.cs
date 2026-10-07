using PdfSharp.Pdf;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// The glyph widths and vertical extent of a font, read from its dictionary, which is what it takes to know
/// where each glyph a content stream shows is drawn.
/// </summary>
internal sealed class PdfFontMetrics
{
    private readonly Dictionary<int, double> _widths = [];
    private double _defaultWidth = 500;
    private double _scale = 0.001;

    /// <summary>
    /// Gets a value indicating whether the font's character codes are two bytes long, as a composite font's are.
    /// </summary>
    public bool TwoByteCodes { get; private set; }

    /// <summary>
    /// Gets the ascent as a fraction of the font size.
    /// </summary>
    public double Ascent { get; private set; } = 0.85;

    /// <summary>
    /// Gets the descent as a fraction of the font size; negative.
    /// </summary>
    public double Descent { get; private set; } = -0.22;

    /// <summary>
    /// Gets the width of a character code in text space units per unit of font size.
    /// </summary>
    /// <param name="code">The character code.</param>
    /// <returns>The width.</returns>
    public double Width(int code)
    {
        return (_widths.TryGetValue(code, out var width) ? width : _defaultWidth) * _scale;
    }

    /// <summary>
    /// Reads a font dictionary.
    /// </summary>
    /// <param name="font">The font dictionary, or <see langword="null"/> when the content names a font it does not define.</param>
    /// <returns>The metrics.</returns>
    public static PdfFontMetrics From(PdfDictionary font)
    {
        var metrics = new PdfFontMetrics();

        if (font is null)
        {
            return metrics;
        }

        var subtype = PdfLowLevel.Text(font.Elements["/Subtype"]);

        if (subtype == "Type0")
        {
            metrics.TwoByteCodes = true;
            metrics._defaultWidth = 1000;

            var descendants = PdfLowLevel.GetArray(font, "/DescendantFonts");
            var descendant = descendants is { Elements.Count: > 0 }
                ? PdfLowLevel.Resolve(descendants.Elements[0]) as PdfDictionary
                : null;

            if (descendant is not null)
            {
                if (descendant.Elements.ContainsKey("/DW"))
                {
                    metrics._defaultWidth = PdfLowLevel.Number(descendant.Elements["/DW"]);
                }

                ReadCompositeWidths(PdfLowLevel.GetArray(descendant, "/W"), metrics._widths);
                metrics.ReadDescriptor(PdfLowLevel.GetDictionary(descendant, "/FontDescriptor"));
            }

            return metrics;
        }

        if (subtype == "Type3")
        {
            var matrix = PdfLowLevel.GetArray(font, "/FontMatrix");

            if (matrix is { Elements.Count: > 0 })
            {
                metrics._scale = PdfLowLevel.Number(matrix.Elements[0]);
            }
        }

        var widths = PdfLowLevel.GetArray(font, "/Widths");

        if (widths is not null)
        {
            var first = (int)PdfLowLevel.Number(font.Elements["/FirstChar"]);

            for (var index = 0; index < widths.Elements.Count; index++)
            {
                metrics._widths[first + index] = PdfLowLevel.Number(widths.Elements[index]);
            }
        }
        else
        {
            // The standard fonts carry no widths of their own; every reader supplies the Adobe metrics.
            var baseFont = PdfLowLevel.Text(font.Elements["/BaseFont"]);

            if (!string.IsNullOrEmpty(baseFont) && Standard14.IsFontInStandard14(baseFont))
            {
                var afm = Standard14.GetAdobeFontMetrics(baseFont);

                foreach (var character in afm.CharacterMetrics.Values)
                {
                    if (character.CharacterCode >= 0)
                    {
                        metrics._widths[character.CharacterCode] = character.Width.X;
                    }
                }

                if (afm.Ascender > 0)
                {
                    metrics.Ascent = afm.Ascender / 1000;
                }

                if (afm.Descender < 0)
                {
                    metrics.Descent = afm.Descender / 1000;
                }
            }
        }

        var descriptor = PdfLowLevel.GetDictionary(font, "/FontDescriptor");

        if (descriptor is not null && descriptor.Elements.ContainsKey("/MissingWidth"))
        {
            metrics._defaultWidth = PdfLowLevel.Number(descriptor.Elements["/MissingWidth"]);
        }

        metrics.ReadDescriptor(descriptor);

        return metrics;
    }

    private void ReadDescriptor(PdfDictionary descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        var ascent = PdfLowLevel.Number(descriptor.Elements["/Ascent"]) / 1000;
        var descent = PdfLowLevel.Number(descriptor.Elements["/Descent"]) / 1000;

        if (ascent > 0.2)
        {
            Ascent = ascent;
        }

        if (descent < 0)
        {
            Descent = descent;
        }
    }

    private static void ReadCompositeWidths(PdfArray widths, Dictionary<int, double> target)
    {
        if (widths is null)
        {
            return;
        }

        var index = 0;

        while (index + 1 < widths.Elements.Count)
        {
            var first = (int)PdfLowLevel.Number(widths.Elements[index]);
            var next = PdfLowLevel.Resolve(widths.Elements[index + 1]);

            // Either "first [w1 w2 …]" or "first last w".
            if (next is PdfArray run)
            {
                for (var offset = 0; offset < run.Elements.Count; offset++)
                {
                    target[first + offset] = PdfLowLevel.Number(run.Elements[offset]);
                }

                index += 2;
            }
            else
            {
                if (index + 2 >= widths.Elements.Count)
                {
                    return;
                }

                var last = (int)PdfLowLevel.Number(next);
                var width = PdfLowLevel.Number(widths.Elements[index + 2]);

                for (var code = first; code <= last && code - first < 65536; code++)
                {
                    target[code] = width;
                }

                index += 3;
            }
        }
    }
}
