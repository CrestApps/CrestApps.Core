using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

internal static class OpenXmlTabularWorksheetReader
{
    // The lowest and highest OLE Automation date serials Excel can represent (1900-01-01 through
    // 9999-12-31). Serials outside this range are treated as plain numbers rather than dates.
    private const double MinExcelDateSerial = 1d;
    private const double MaxExcelDateSerial = 2958465d;

    private static readonly XmlReaderSettings _xmlReaderSettings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
    };

    public static string[] CreateSharedStringCache(WorkbookPart workbookPart)
    {
        var table = workbookPart.SharedStringTablePart?.SharedStringTable;

        if (table == null)
        {
            return null;
        }

        var cache = new string[table.ChildElements.Count];
        var index = 0;

        foreach (SharedStringItem item in table.Elements<SharedStringItem>())
        {
            cache[index++] = item.InnerText;
        }

        return cache;
    }

    // Worksheets are resolved through the workbook's <sheets> declaration, not WorksheetParts, so
    // names and workbook order are preserved.
    public static void ReadWorksheets(
        WorkbookPart workbookPart,
        string fileName,
        ILogger logger,
        Action<string> onWorksheetStart,
        Action<List<string>> onRow,
        Action onWorksheetEnd,
        CancellationToken cancellationToken)
    {
        var sharedStrings = CreateSharedStringCache(workbookPart);
        var dateStyles = BuildDateStyleTable(workbookPart);
        var expectedColumnCount = 16;

        void ReadWorksheet(WorksheetPart worksheetPart, string worksheetName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onWorksheetStart(worksheetName);
            var sheetRowCount = 0;

            using var stream = worksheetPart.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(stream, _xmlReaderSettings);

            while (!reader.EOF)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "row", StringComparison.Ordinal))
                {
                    reader.Read();

                    continue;
                }

                var row = ReadRow(reader, sharedStrings, dateStyles, expectedColumnCount, out var hasValue);

                if (!hasValue)
                {
                    continue;
                }

                expectedColumnCount = Math.Max(expectedColumnCount, row.Count);
                onRow(row);
                sheetRowCount++;
            }

            onWorksheetEnd();

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "OpenXml tabular reader read {RowCount} non-empty row(s) from worksheet '{WorksheetName}' for '{FileName}'.",
                    sheetRowCount,
                    worksheetName ?? worksheetPart.Uri?.ToString(),
                    fileName);
            }
        }

        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>().ToList();

        if (sheets is { Count: > 0 })
        {
            foreach (var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Hidden and very-hidden sheets are typically lookup/scratch tables the author chose not
                // to show, so they are skipped by default rather than imported as opaque extra tables.
                if (sheet.State?.Value is SheetStateValues state && state != SheetStateValues.Visible)
                {
                    continue;
                }

                if (sheet.Id?.Value is not string relationshipId ||
                    workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
                {
                    continue;
                }

                ReadWorksheet(worksheetPart, sheet.Name?.Value);
            }

            return;
        }

        // Fallback for malformed workbooks that omit the <sheets> declaration: enumerate the parts
        // directly. Worksheet names are unavailable in this path.
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            ReadWorksheet(worksheetPart, null);
        }
    }

    private static List<string> ReadRow(
        XmlReader reader,
        string[] sharedStrings,
        bool[] dateStyles,
        int expectedColumnCount,
        out bool hasValue)
    {
        hasValue = false;
        var values = new List<string>(expectedColumnCount);

        // The reader is positioned on the <row> start element. A self-closing row such as
        // <row r="1"/> carries no cells, so it is skipped without descending into it. Reading cells
        // inline (rather than via ReadSubtree + Skip) keeps the reader correctly positioned so a blank
        // leading row never causes the following header row to be swallowed.
        if (reader.IsEmptyElement)
        {
            reader.Read();

            return values;
        }

        var rowDepth = reader.Depth;
        reader.Read();

        while (true)
        {
            if (reader.EOF ||
                (reader.NodeType == XmlNodeType.EndElement &&
                    reader.Depth == rowDepth &&
                    string.Equals(reader.LocalName, "row", StringComparison.Ordinal)))
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
            {
                reader.Read();

                continue;
            }

            var columnIndex = GetColumnIndex(
                reader.GetAttribute("r"),
                values.Count);

            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            var isDateStyle = IsDateStyledCell(reader.GetAttribute("s"), dateStyles);
            var value = GetCellValue(reader, reader.GetAttribute("t"), isDateStyle, sharedStrings);

            values.Add(value);
            hasValue |= !string.IsNullOrEmpty(value);
            reader.Read();
        }

        // Advance past the closing </row> element so the outer loop resumes on the next row.
        reader.Read();

        TrimTrailingEmptyValues(values);

        return values;
    }

    private static void TrimTrailingEmptyValues(List<string> values)
    {
        var last = values.Count - 1;

        while (last >= 0 && string.IsNullOrEmpty(values[last]))
        {
            last--;
        }

        var removeCount = values.Count - last - 1;

        if (removeCount > 0)
        {
            values.RemoveRange(
                last + 1,
                removeCount);
        }
    }

    private static int GetColumnIndex(string cellReference, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return fallbackIndex;
        }

        var columnIndex = 0;
        var foundColumn = false;

        foreach (var c in cellReference)
        {
            if (c >= 'A' && c <= 'Z')
            {
                columnIndex = columnIndex * 26 + c - 'A' + 1;
                foundColumn = true;
            }
            else if (c >= 'a' && c <= 'z')
            {
                columnIndex = columnIndex * 26 + c - 'a' + 1;
                foundColumn = true;
            }
            else
            {
                break;
            }
        }

        return foundColumn
            ? columnIndex - 1
            : fallbackIndex;
    }

    private static string GetCellValue(
        XmlReader reader,
        string cellType,
        bool isDateStyle,
        string[] sharedStrings)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var cellDepth = reader.Depth;
        string value = null;
        string inlineText = null;
        StringBuilder inlineBuilder = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement &&
                reader.Depth == cellDepth &&
                string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.LocalName, "v", StringComparison.Ordinal))
            {
                value = reader.ReadElementContentAsString();

                if (reader.NodeType == XmlNodeType.EndElement &&
                    reader.Depth == cellDepth &&
                    string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
                {
                    break;
                }

                continue;
            }

            if (!string.Equals(cellType, "inlineStr", StringComparison.Ordinal) ||
                !string.Equals(reader.LocalName, "t", StringComparison.Ordinal))
            {
                continue;
            }

            var text = reader.ReadElementContentAsString();

            if (inlineBuilder != null)
            {
                inlineBuilder.Append(text);
            }
            else if (inlineText == null)
            {
                inlineText = text;
            }
            else
            {
                inlineBuilder = new StringBuilder(inlineText.Length + text.Length);
                inlineBuilder.Append(inlineText);
                inlineBuilder.Append(text);
            }

            if (reader.NodeType == XmlNodeType.EndElement &&
                reader.Depth == cellDepth &&
                string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (inlineBuilder != null)
        {
            value = inlineBuilder.ToString();
        }
        else if (inlineText != null)
        {
            value = inlineText;
        }

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (string.Equals(cellType, "s", StringComparison.Ordinal) &&
            sharedStrings != null &&
            int.TryParse(value, out var index) &&
            (uint)index < (uint)sharedStrings.Length)
        {
            return sharedStrings[index];
        }

        if (string.Equals(cellType, "b", StringComparison.Ordinal))
        {
            return value == "1"
                ? "TRUE"
                : "FALSE";
        }

        // A numeric cell (no explicit type, or an explicit "n") carrying a date/time number format is an
        // Excel date serial. Converting it to an ISO string keeps date questions answerable instead of
        // leaving an opaque integer like 46266 in the data.
        if ((cellType is null || string.Equals(cellType, "n", StringComparison.Ordinal)) &&
            isDateStyle &&
            TryConvertExcelDate(value, out var isoDate))
        {
            return isoDate;
        }

        return value;
    }

    private static bool IsDateStyledCell(string styleAttribute, bool[] dateStyles)
    {
        if (dateStyles is null ||
            string.IsNullOrEmpty(styleAttribute) ||
            !int.TryParse(styleAttribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex))
        {
            return false;
        }

        return (uint)styleIndex < (uint)dateStyles.Length && dateStyles[styleIndex];
    }

    private static bool TryConvertExcelDate(string value, out string isoDate)
    {
        isoDate = null;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) ||
            serial < MinExcelDateSerial ||
            serial > MaxExcelDateSerial)
        {
            return false;
        }

        try
        {
            var date = DateTime.FromOADate(serial);
            isoDate = date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Builds a lookup keyed by cell-format (style) index indicating whether that style renders its value
    // as a date/time. Both the built-in date format ids and workbook-specific custom formats are honored.
    private static bool[] BuildDateStyleTable(WorkbookPart workbookPart)
    {
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        var cellFormats = stylesheet?.CellFormats;

        if (cellFormats is null)
        {
            return null;
        }

        var customDateFormats = new Dictionary<uint, bool>();

        if (stylesheet.NumberingFormats is not null)
        {
            foreach (var numberingFormat in stylesheet.NumberingFormats.Elements<NumberingFormat>())
            {
                if (numberingFormat.NumberFormatId?.Value is uint formatId &&
                    numberingFormat.FormatCode?.Value is string formatCode)
                {
                    customDateFormats[formatId] = IsDateFormatCode(formatCode);
                }
            }
        }

        var formats = cellFormats.Elements<CellFormat>().ToList();
        var table = new bool[formats.Count];

        for (var i = 0; i < formats.Count; i++)
        {
            var formatId = formats[i].NumberFormatId?.Value ?? 0;

            table[i] = IsBuiltInDateFormat(formatId) ||
                (customDateFormats.TryGetValue(formatId, out var isDate) && isDate);
        }

        return table;
    }

    private static bool IsBuiltInDateFormat(uint numberFormatId)
    {
        // Built-in Excel date/time format ids: 14-22 (dates and times) and 45-47 (elapsed times).
        return numberFormatId is (>= 14 and <= 22) or (>= 45 and <= 47);
    }

    private static bool IsDateFormatCode(string formatCode)
    {
        if (string.IsNullOrEmpty(formatCode))
        {
            return false;
        }

        // Number formats never contain date/time placeholder letters, so the presence of any y/m/d/h/s
        // token (outside quoted literals, escapes, and [bracketed] sections) marks a date or time format.
        var inLiteral = false;
        var inBracket = false;

        for (var i = 0; i < formatCode.Length; i++)
        {
            var c = formatCode[i];

            if (inLiteral)
            {
                if (c == '"')
                {
                    inLiteral = false;
                }

                continue;
            }

            if (inBracket)
            {
                if (c == ']')
                {
                    inBracket = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inLiteral = true;

                    break;
                case '[':
                    inBracket = true;

                    break;
                case '\\':
                    i++;

                    break;
                case 'y' or 'Y' or 'm' or 'M' or 'd' or 'D' or 'h' or 'H' or 's' or 'S':
                    return true;
            }
        }

        return false;
    }
}
