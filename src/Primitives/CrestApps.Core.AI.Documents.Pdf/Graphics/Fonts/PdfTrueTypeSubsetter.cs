using System.Buffers.Binary;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// Makes a TrueType font smaller for embedding: the glyphs a document does not use are emptied, and the
/// tables a PDF reader does not need are left out.
/// </summary>
/// <remarks>
/// Glyphs keep their numbers, so the text written with the full font draws the same with the subset; the
/// parts composite glyphs are made of are kept with them.
/// </remarks>
internal static class PdfTrueTypeSubsetter
{
    // The tables a PDF reader draws a TrueType font from (PDF 32000-1, 9.9).
    private static readonly string[] _kept = ["cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "prep"];

    private const int ArgumentsAreWords = 0x0001;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXAndYScale = 0x0040;
    private const int HasTwoByTwo = 0x0080;

    /// <summary>
    /// Builds the subset.
    /// </summary>
    /// <param name="font">The font file, with TrueType outlines.</param>
    /// <param name="glyphs">The glyphs to keep. The missing-glyph glyph, 0, is always kept.</param>
    /// <returns>The subset font file, or <see langword="null"/> when the font cannot be subset.</returns>
    public static byte[] Subset(byte[] font, IEnumerable<int> glyphs)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(glyphs);

        var tables = PdfTrueTypeFile.ReadTables(font);

        if (!tables.TryGetValue("head", out var head) || head.Length < 54 ||
            !tables.TryGetValue("maxp", out var maxp) || maxp.Length < 6 ||
            !tables.TryGetValue("loca", out var loca) ||
            !tables.TryGetValue("glyf", out var glyf))
        {
            return null;
        }

        var data = font.AsSpan();
        var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data[(maxp.Offset + 4)..]);
        var longOffsets = BinaryPrimitives.ReadInt16BigEndian(data[(head.Offset + 50)..]) == 1;
        var offsets = ReadOffsets(data.Slice(loca.Offset, loca.Length), glyphCount, longOffsets);
        var used = Close(data.Slice(glyf.Offset, glyf.Length), offsets, glyphs.Append(0));

        using var newGlyf = new MemoryStream();
        var newLoca = new byte[(glyphCount + 1) * 4];

        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyph * 4), (uint)newGlyf.Length);

            if (!used.Contains(glyph))
            {
                continue;
            }

            var start = offsets[glyph];
            var end = offsets[glyph + 1];

            if (end > start && end <= glyf.Length)
            {
                newGlyf.Write(data.Slice(glyf.Offset + start, end - start));

                while (newGlyf.Length % 4 != 0)
                {
                    newGlyf.WriteByte(0);
                }
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyphCount * 4), (uint)newGlyf.Length);

        var newHead = data.Slice(head.Offset, head.Length).ToArray();

        // The new location table holds 32-bit offsets.
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(50), 1);

        var output = new List<(string Tag, ReadOnlyMemory<byte> Data)>();

        foreach (var tag in _kept)
        {
            switch (tag)
            {
                case "glyf":
                    output.Add((tag, newGlyf.ToArray()));

                    break;

                case "loca":
                    output.Add((tag, newLoca));

                    break;

                case "head":
                    output.Add((tag, newHead));

                    break;

                default:
                    if (tables.TryGetValue(tag, out var table))
                    {
                        output.Add((tag, font.AsMemory(table.Offset, table.Length)));
                    }

                    break;
            }
        }

        return PdfTrueTypeFile.Build(0x00010000, output);
    }

    private static int[] ReadOffsets(ReadOnlySpan<byte> loca, int glyphCount, bool longOffsets)
    {
        var offsets = new int[glyphCount + 1];

        for (var index = 0; index <= glyphCount; index++)
        {
            if (longOffsets)
            {
                offsets[index] = (index * 4) + 4 <= loca.Length
                    ? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(loca[(index * 4)..]), int.MaxValue)
                    : offsets[Math.Max(0, index - 1)];
            }
            else
            {
                offsets[index] = (index * 2) + 2 <= loca.Length
                    ? BinaryPrimitives.ReadUInt16BigEndian(loca[(index * 2)..]) * 2
                    : offsets[Math.Max(0, index - 1)];
            }
        }

        return offsets;
    }

    /// <summary>
    /// Adds to the glyphs asked for every glyph a composite one among them is made of.
    /// </summary>
    private static HashSet<int> Close(ReadOnlySpan<byte> glyf, int[] offsets, IEnumerable<int> glyphs)
    {
        var used = new HashSet<int>();
        var pending = new Stack<int>();

        foreach (var glyph in glyphs)
        {
            if (glyph >= 0 && glyph < offsets.Length - 1 && used.Add(glyph))
            {
                pending.Push(glyph);
            }
        }

        while (pending.Count > 0)
        {
            var glyph = pending.Pop();
            var start = offsets[glyph];
            var end = Math.Min(offsets[glyph + 1], glyf.Length);

            if (end - start < 10 || BinaryPrimitives.ReadInt16BigEndian(glyf[start..]) >= 0)
            {
                continue;
            }

            var position = start + 10;

            while (position + 4 <= end)
            {
                var flags = BinaryPrimitives.ReadUInt16BigEndian(glyf[position..]);
                var component = BinaryPrimitives.ReadUInt16BigEndian(glyf[(position + 2)..]);

                if (component < offsets.Length - 1 && used.Add(component))
                {
                    pending.Push(component);
                }

                // The flags and the glyph index, then the two offsets, as bytes or words.
                position += (flags & ArgumentsAreWords) != 0
                    ? 8
                    : 6;

                if ((flags & HasScale) != 0)
                {
                    position += 2;
                }
                else if ((flags & HasXAndYScale) != 0)
                {
                    position += 4;
                }
                else if ((flags & HasTwoByTwo) != 0)
                {
                    position += 8;
                }

                if ((flags & MoreComponents) == 0)
                {
                    break;
                }
            }
        }

        return used;
    }
}
