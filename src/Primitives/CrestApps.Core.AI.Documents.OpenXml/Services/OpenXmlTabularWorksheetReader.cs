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

    public static void ReadNonEmptyRows(
        WorkbookPart workbookPart,
        string fileName,
        ILogger logger,
        Action<List<string>, bool> rowHandler,
        CancellationToken cancellationToken)
    {
        var sharedStrings = CreateSharedStringCache(workbookPart);
        var expectedColumnCount = 16;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstNonEmptyRowInWorksheet = true;
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

                using var rowReader = reader.ReadSubtree();
                var row = ReadRow(rowReader, sharedStrings, expectedColumnCount, out var hasValue);
                reader.Skip();

                if (!hasValue)
                {
                    continue;
                }

                expectedColumnCount = Math.Max(expectedColumnCount, row.Count);
                rowHandler(row, firstNonEmptyRowInWorksheet);
                firstNonEmptyRowInWorksheet = false;
                sheetRowCount++;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "OpenXml tabular builder read {RowCount} non-empty row(s) from worksheet '{WorksheetUri}' for '{FileName}'.",
                    sheetRowCount,
                    worksheetPart.Uri,
                    fileName);
            }
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

        if (!reader.Read() || reader.IsEmptyElement)
        {
            return values;
        }

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
            {
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
        }

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
