using System.Globalization;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an Open XML word-processing document (<c>.docx</c>).
/// <para>
/// The body text is parsed before it is laid out, so Markdown and HTML become real headings, lists,
/// quotes, code blocks, rules, tables, and emphasis. Writing the string out verbatim instead would put
/// raw markup in front of the reader, which is what the previous line-per-paragraph approach did.
/// </para>
/// <para>
/// The document is built through the same styles, numbering and table writer the Word agent uses, so a
/// file written here in one call has the structure — heading styles a table of contents reads, real lists,
/// tables whose values carry the spreadsheet export's number formats — of one the agent builds up over a
/// conversation, and the agent can open it and keep working on it.
/// </para>
/// </summary>
public sealed class WordGeneratedFileWriter : IGeneratedFileWriter
{
    private static readonly SpreadsheetColumnFormat _countFormat = new()
    {
        NumberFormat = SpreadsheetNumberFormat.Number,
        Decimals = 0,
    };

    /// <summary>
    /// Writes the content as an Open XML word-processing document to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] bytes;

        using (var package = WordPackage.Create(new WordDesign()))
        {
            var section = package.Body.GetFirstChild<SectionProperties>();
            var writer = new WordBlockWriter(package.MainPart, new WordDesign(), TextWidth(section));

            void Add(OpenXmlElement element) => section.InsertBeforeSelf(element);

            // The title and the sheet names are written as they are: unlike the body, they are not Markdown, so
            // a backslash, an asterisk or an underscore in them is part of the name.
            if (!string.IsNullOrWhiteSpace(content.Title))
            {
                Add(writer.Paragraph([new RichTextSpan(content.Title.Trim())], writer.Style(WordStyleSheet.Title)));
            }

            if (!string.IsNullOrEmpty(content.Text))
            {
                foreach (var element in writer.FromRichText(RichTextParser.Parse(content.Text)))
                {
                    Add(element);
                }
            }

            if (content.HasTable)
            {
                var sheets = content.GetSheets();

                foreach (var sheet in sheets)
                {
                    if (!sheet.HasTable)
                    {
                        continue;
                    }

                    // With several tables in one document, each is titled so the reader can tell them
                    // apart.
                    if (sheets.Count > 1 && !string.IsNullOrWhiteSpace(sheet.Name))
                    {
                        Add(writer.Paragraph([new RichTextSpan(sheet.Name.Trim())], writer.Style(WordStyleSheet.Heading(2))));
                    }

                    Add(writer.Table(ToTableSpec(sheet)));
                    Add(new Paragraph());
                }
            }

            bytes = package.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();

        await destination.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Describes a generated sheet as a table: its column formats, header style, banding and total row are
    /// carried over, so the table reads the way the spreadsheet export of the same data reads. The headers and
    /// values are data, written as they are rather than read as Markdown.
    /// </summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The table description.</returns>
    internal static WordTableSpec ToTableSpec(GeneratedSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var formatting = sheet.Formatting;
        var spec = new WordTableSpec
        {
            Style = formatting?.StyleHeader == false ? "grid" : "data",
            Banded = formatting?.BandedRows,
            BandFill = formatting?.BandColor,
            HeaderFill = formatting?.HeaderStyle?.BackgroundColor,
            HeaderTextColor = formatting?.HeaderStyle?.FontColor,
            Literal = true,
        };

        var formats = new List<SpreadsheetColumnFormat>(sheet.Header.Count);

        foreach (var header in sheet.Header)
        {
            var format = formatting?.FindColumn(header);

            formats.Add(format);
            spec.Columns.Add(new WordTableColumn
            {
                Header = header ?? string.Empty,
                Format = format,
                Alignment = ReadAlignment(format?.Style?.Alignment),

                // A spreadsheet width is in characters; as a relative weight it keeps the columns' proportions.
                Width = format?.Width is > 0 ? format.Width.Value.ToString(CultureInfo.InvariantCulture) : null,
            });
        }

        foreach (var row in sheet.Rows ?? [])
        {
            var cells = new List<WordTableCell>(sheet.Header.Count);

            for (var index = 0; index < sheet.Header.Count; index++)
            {
                cells.Add(new WordTableCell { Value = index < row.Count ? row[index] ?? string.Empty : string.Empty });
            }

            spec.Rows.Add(cells);
        }

        if (formatting?.TotalRow is { } totalRow && sheet.Rows is { Count: > 0 })
        {
            spec.Rows.Add(CreateTotalRow(sheet, totalRow));
        }

        return spec;
    }

    private static List<WordTableCell> CreateTotalRow(GeneratedSheet sheet, SpreadsheetTotalRow totalRow)
    {
        var cells = new List<WordTableCell>(sheet.Header.Count);

        for (var index = 0; index < sheet.Header.Count; index++)
        {
            var header = sheet.Header[index];
            var total = totalRow.Columns.FirstOrDefault(column => SpreadsheetFormatting.NameMatches(column.Column, header));
            var cell = new WordTableCell
            {
                Bold = totalRow.Style?.Bold ?? true,
                Fill = totalRow.Style?.BackgroundColor,
                Color = totalRow.Style?.FontColor,
            };

            if (total is not null && total.Function == SpreadsheetAggregateFunction.Count)
            {
                // A count is of the cells that hold anything, as the spreadsheet's SUBTOTAL(103) counts them,
                // and it is a whole number whatever the column's format: three amounts are 3, not $3.00.
                cell.Value = sheet.Rows.Count(row => index < row.Count && !string.IsNullOrEmpty(row[index]));
                cell.Format = _countFormat;
            }
            else if (total is not null)
            {
                var values = sheet.Rows
                    .Select(row => index < row.Count && double.TryParse(row[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null)
                    .Where(number => number is not null)
                    .Select(number => number.Value)
                    .ToList();

                cell.Value = total.Function switch
                {
                    SpreadsheetAggregateFunction.Average when values.Count > 0 => values.Average(),
                    SpreadsheetAggregateFunction.Min when values.Count > 0 => values.Min(),
                    SpreadsheetAggregateFunction.Max when values.Count > 0 => values.Max(),
                    SpreadsheetAggregateFunction.Sum => values.Sum(),
                    _ => string.Empty,
                };
            }
            else if (index == 0)
            {
                cell.Text = string.IsNullOrWhiteSpace(totalRow.Label) ? "Total" : totalRow.Label;
            }

            cells.Add(cell);
        }

        return cells;
    }

    private static string ReadAlignment(SpreadsheetHorizontalAlignment? alignment)
    {
        return alignment switch
        {
            SpreadsheetHorizontalAlignment.Left => "left",
            SpreadsheetHorizontalAlignment.Center => "center",
            SpreadsheetHorizontalAlignment.Right => "right",
            _ => null,
        };
    }

    private static int TextWidth(SectionProperties section)
    {
        var size = section?.GetFirstChild<PageSize>();
        var margin = section?.GetFirstChild<PageMargin>();
        var width = (int)(size?.Width?.Value ?? 12240U);
        var left = (int)(margin?.Left?.Value ?? 1440U);
        var right = (int)(margin?.Right?.Value ?? 1440U);

        return Math.Max(1440, width - left - right);
    }
}
