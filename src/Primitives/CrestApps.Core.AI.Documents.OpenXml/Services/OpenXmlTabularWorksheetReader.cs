using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

internal static class OpenXmlTabularWorksheetReader
{
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

    /// <summary>
    /// Enumerates the worksheets of a workbook in workbook order, invoking callbacks that delimit each
    /// worksheet and deliver its non-empty rows. Worksheets are resolved through the workbook's
    /// <c>&lt;sheets&gt;</c> declaration so their names and order are preserved, rather than iterating
    /// <see cref="WorkbookPart.WorksheetParts"/> whose order is undefined.
    /// </summary>
    /// <param name="workbookPart">The workbook part to read.</param>
    /// <param name="fileName">The source file name, used for logging.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="onWorksheetStart">Invoked with the worksheet name before any of its rows are read.</param>
    /// <param name="onRow">Invoked once for each non-empty row, in source order.</param>
    /// <param name="onWorksheetEnd">Invoked after the last row of a worksheet has been read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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

                var row = ReadRow(reader, sharedStrings, expectedColumnCount, out var hasValue);

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

            var value = GetCellValue(reader, reader.GetAttribute("t"), sharedStrings);

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

        return value;
    }
}
