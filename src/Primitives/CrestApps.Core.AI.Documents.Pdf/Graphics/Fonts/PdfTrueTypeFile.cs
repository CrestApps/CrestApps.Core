using System.Buffers.Binary;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// Reads the table directory, the names and the style of a TrueType or OpenType file, and takes one face
/// out of a font collection (<c>.ttc</c>) as a file of its own.
/// </summary>
internal static class PdfTrueTypeFile
{
    private const uint CollectionTag = 0x74746366;
    private const uint OpenTypeCffTag = 0x4F54544F;

    /// <summary>
    /// Returns whether the bytes are a font collection.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <returns><see langword="true"/> for a <c>.ttc</c>.</returns>
    public static bool IsCollection(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 && BinaryPrimitives.ReadUInt32BigEndian(data) == CollectionTag;
    }

    /// <summary>
    /// Returns whether a face draws its glyphs with PostScript (CFF) outlines rather than TrueType ones.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="faceOffset">Where the face's table directory starts.</param>
    /// <returns><see langword="true"/> for a CFF-based OpenType face.</returns>
    public static bool IsCff(ReadOnlySpan<byte> data, int faceOffset = 0)
    {
        return data.Length >= faceOffset + 4 && BinaryPrimitives.ReadUInt32BigEndian(data[faceOffset..]) == OpenTypeCffTag;
    }

    /// <summary>
    /// Lists where each face's table directory starts: one face for a font file, every face of a collection.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <returns>The offsets.</returns>
    public static List<int> FaceOffsets(ReadOnlySpan<byte> data)
    {
        if (!IsCollection(data))
        {
            return data.Length >= 12 ? [0] : [];
        }

        var count = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[8..]), 256);
        var offsets = new List<int>(count);

        for (var index = 0; index < count && 12 + (index * 4) + 4 <= data.Length; index++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(data[(12 + (index * 4))..]);

            if (offset < data.Length)
            {
                offsets.Add((int)offset);
            }
        }

        return offsets;
    }

    /// <summary>
    /// Reads a face's table directory.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="faceOffset">Where the face's table directory starts.</param>
    /// <returns>Each table's offset and length, by tag.</returns>
    public static Dictionary<string, (int Offset, int Length)> ReadTables(ReadOnlySpan<byte> data, int faceOffset = 0)
    {
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);

        if (data.Length < faceOffset + 12)
        {
            return tables;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(data[(faceOffset + 4)..]);

        for (var index = 0; index < count; index++)
        {
            var record = faceOffset + 12 + (index * 16);

            if (record + 16 > data.Length)
            {
                break;
            }

            var tag = Encoding.ASCII.GetString(data.Slice(record, 4));
            var offset = BinaryPrimitives.ReadUInt32BigEndian(data[(record + 8)..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(record + 12)..]);

            if (offset + (long)length <= data.Length)
            {
                tables[tag] = ((int)offset, (int)length);
            }
        }

        return tables;
    }

    /// <summary>
    /// Takes one face of a collection out as a font file of its own. A font file is returned as it is.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="faceIndex">The face's index in the collection.</param>
    /// <returns>The face's file, or <see langword="null"/> when the collection has no such face.</returns>
    public static byte[] ExtractFace(byte[] data, int faceIndex)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsCollection(data))
        {
            return faceIndex == 0
                ? data
                : null;
        }

        var offsets = FaceOffsets(data);

        if (faceIndex < 0 || faceIndex >= offsets.Count)
        {
            return null;
        }

        var faceOffset = offsets[faceIndex];
        var tables = ReadTables(data, faceOffset);
        var sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(faceOffset));

        return Build(sfntVersion, tables.Select(table => (table.Key, (ReadOnlyMemory<byte>)data.AsMemory(table.Value.Offset, table.Value.Length))));
    }

    /// <summary>
    /// Writes a font file from its tables, ordered by tag with their checksums and the header's checksum
    /// adjustment computed.
    /// </summary>
    /// <param name="sfntVersion">The version the file starts with: 0x00010000 for TrueType outlines.</param>
    /// <param name="tables">The tables, by tag.</param>
    /// <returns>The file.</returns>
    public static byte[] Build(uint sfntVersion, IEnumerable<(string Tag, ReadOnlyMemory<byte> Data)> tables)
    {
        var ordered = tables.OrderBy(table => table.Tag, StringComparer.Ordinal).ToList();
        var count = ordered.Count;
        var power = 1;
        var log = 0;

        while (power * 2 <= count)
        {
            power *= 2;
            log++;
        }

        var headerLength = 12 + (count * 16);
        var total = headerLength + ordered.Sum(table => (table.Data.Length + 3) & ~3);
        var file = new byte[total];
        var span = file.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)((count * 16) - (power * 16)));

        var offset = headerLength;
        var headOffset = -1;

        for (var index = 0; index < count; index++)
        {
            var (tag, data) = ordered[index];
            var record = span[(12 + (index * 16))..];

            data.Span.CopyTo(span[offset..]);

            if (tag == "head" && data.Length >= 12)
            {
                // The adjustment is summed as zero, and written once the whole file's sum is known.
                BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 8)..], 0);
                headOffset = offset;
            }

            Encoding.ASCII.GetBytes(tag.PadRight(4).AsSpan(0, 4), record);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(span.Slice(offset, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
            offset += (data.Length + 3) & ~3;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], unchecked(0xB1B0AFBA - Checksum(span)));
        }

        return file;
    }

    /// <summary>
    /// Reads a face's family name, style name and PostScript name from its name table.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="faceOffset">Where the face's table directory starts.</param>
    /// <returns>The names, each <see langword="null"/> when the face does not give it.</returns>
    public static (string Family, string Style, string PostScript) ReadNames(ReadOnlySpan<byte> data, int faceOffset = 0)
    {
        var tables = ReadTables(data, faceOffset);

        return tables.TryGetValue("name", out var name)
            ? ParseNames(data.Slice(name.Offset, name.Length))
            : (null, null, null);
    }

    /// <summary>
    /// Reads every face of a font file from disk, reading only the tables that name and style it.
    /// </summary>
    /// <param name="stream">The file, open for reading and seeking.</param>
    /// <returns>Each face's index in the file, family name and style.</returns>
    public static List<(int Index, string Family, bool Bold, bool Italic)> ReadFaces(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var faces = new List<(int Index, string Family, bool Bold, bool Italic)>();
        var header = ReadAt(stream, 0, 12);

        if (header.Length < 12)
        {
            return faces;
        }

        var offsets = IsCollection(header)
            ? FaceOffsets(ReadAt(stream, 0, 12 + ((int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)), 256) * 4)))
            : [0];

        for (var index = 0; index < offsets.Count; index++)
        {
            var directory = ReadAt(stream, offsets[index], 12);

            if (directory.Length < 12 || IsCff(directory))
            {
                continue;
            }

            var count = BinaryPrimitives.ReadUInt16BigEndian(directory.AsSpan(4));
            var records = ReadAt(stream, offsets[index], 12 + (count * 16));
            var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);

            for (var record = 0; record < count && 12 + (record * 16) + 16 <= records.Length; record++)
            {
                var entry = records.AsSpan(12 + (record * 16));

                tables[Encoding.ASCII.GetString(entry[..4])] = ((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]));
            }

            if (!tables.TryGetValue("name", out var name) || !tables.ContainsKey("glyf"))
            {
                continue;
            }

            var (family, _, _) = ParseNames(ReadAt(stream, name.Offset, name.Length));

            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }

            var os2 = tables.TryGetValue("OS/2", out var os2Table)
                ? ReadAt(stream, os2Table.Offset, os2Table.Length)
                : [];

            var head = tables.TryGetValue("head", out var headTable)
                ? ReadAt(stream, headTable.Offset, headTable.Length)
                : [];

            var (bold, italic) = ParseStyle(os2, head);

            faces.Add((index, family, bold, italic));
        }

        return faces;
    }

    /// <summary>
    /// Reads the family name, style name and PostScript name from a name table.
    /// </summary>
    /// <param name="table">The name table.</param>
    /// <returns>The names, each <see langword="null"/> when the table does not give it.</returns>
    public static (string Family, string Style, string PostScript) ParseNames(ReadOnlySpan<byte> table)
    {
        if (table.Length < 6)
        {
            return (null, null, null);
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        var storage = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        var found = new Dictionary<int, (int Rank, string Value)>();

        for (var index = 0; index < count; index++)
        {
            var record = 6 + (index * 12);

            if (record + 12 > table.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(table[record..]);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 2)..]);
            var language = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 4)..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 6)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 8)..]);
            var start = storage + BinaryPrimitives.ReadUInt16BigEndian(table[(record + 10)..]);

            // The family (1) groups a face with at most three others, regular, bold, italic and bold italic, the
            // way applications list fonts; "Segoe UI Semibold" is a family of its own.
            if (nameId is not (1 or 2 or 6) || start + length > table.Length)
            {
                continue;
            }

            var bytes = table.Slice(start, length);
            var isUnicode = platform == 0 || (platform == 3 && encoding is 1 or 10);

            if (!isUnicode && (platform, encoding) != (1, 0))
            {
                continue;
            }

            // English Windows names come first, then other Unicode ones, then the old Macintosh ones.
            var rank = !isUnicode
                ? 2
                : platform == 0 || language == 0x409
                    ? 0
                    : 1;

            var value = isUnicode
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.Latin1.GetString(bytes);

            if (!found.TryGetValue(nameId, out var current) || rank < current.Rank)
            {
                found[nameId] = (rank, value.Trim('\0', ' '));
            }
        }

        string Get(int id)
        {
            return found.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value.Value)
                ? value.Value
                : null;
        }

        return (Get(1), Get(2), Get(6));
    }

    /// <summary>
    /// Reads whether a face is bold and whether it is italic, from its OS/2 and header tables.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="faceOffset">Where the face's table directory starts.</param>
    /// <returns>The style.</returns>
    public static (bool Bold, bool Italic) ReadStyle(ReadOnlySpan<byte> data, int faceOffset = 0)
    {
        var tables = ReadTables(data, faceOffset);

        var os2 = tables.TryGetValue("OS/2", out var os2Table)
            ? data.Slice(os2Table.Offset, os2Table.Length)
            : [];

        var head = tables.TryGetValue("head", out var headTable)
            ? data.Slice(headTable.Offset, headTable.Length)
            : [];

        return ParseStyle(os2, head);
    }

    /// <summary>
    /// Reads whether a face is bold and whether it is italic from its OS/2 table, or else its header.
    /// </summary>
    /// <param name="os2">The OS/2 table, or empty.</param>
    /// <param name="head">The header table, or empty.</param>
    /// <returns>The style.</returns>
    public static (bool Bold, bool Italic) ParseStyle(ReadOnlySpan<byte> os2, ReadOnlySpan<byte> head)
    {
        if (os2.Length >= 64)
        {
            var selection = BinaryPrimitives.ReadUInt16BigEndian(os2[62..]);
            var weight = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]);

            return ((selection & 0x20) != 0 || weight >= 600, (selection & 0x01) != 0);
        }

        if (head.Length >= 46)
        {
            var macStyle = BinaryPrimitives.ReadUInt16BigEndian(head[44..]);

            return ((macStyle & 0x01) != 0, (macStyle & 0x02) != 0);
        }

        return (false, false);
    }

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= stream.Length)
        {
            return [];
        }

        var buffer = new byte[(int)Math.Min(length, stream.Length - offset)];

        stream.Position = offset;
        stream.ReadExactly(buffer);

        return buffer;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;

        for (var index = 0; index + 4 <= data.Length; index += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[index..]));
        }

        if (data.Length % 4 != 0)
        {
            Span<byte> tail = stackalloc byte[4];

            data[(data.Length & ~3)..].CopyTo(tail);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(tail));
        }

        return sum;
    }
}
