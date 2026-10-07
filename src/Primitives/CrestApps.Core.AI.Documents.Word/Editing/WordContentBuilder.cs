using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Charts;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Builds the content a tool call describes as JSON blocks — headings, paragraphs, Markdown, lists, quotes,
/// code, tables, pictures, charts, captions, page breaks and rules — as elements of the document being edited.
/// </summary>
internal sealed class WordContentBuilder
{
    /// <summary>
    /// The block types content can be described with.
    /// </summary>
    public static readonly IReadOnlyList<string> BlockTypes =
    [
        "heading", "title", "subtitle", "paragraph", "markdown", "bullet_list", "numbered_list", "quote", "code",
        "table", "image", "chart", "caption", "page_break", "rule",
    ];

    private const double MinChartSize = 18;

    private const double MaxChartHeight = 1584;

    private readonly WordEditContext _edit;
    private readonly WordToolContext _tools;
    private readonly WordBlockWriter _writer;
    private WordDrawingIds _drawingIds;
    private WordBookmarks _bookmarks;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordContentBuilder"/> class.
    /// </summary>
    /// <param name="edit">The edit in progress.</param>
    /// <param name="tools">The tool context, for pictures and tabular data.</param>
    /// <param name="anchor">An element in the section the content goes into, or <see langword="null"/> for the last section.</param>
    public WordContentBuilder(WordEditContext edit, WordToolContext tools, OpenXmlElement anchor = null)
    {
        _edit = edit;
        _tools = tools;
        _writer = edit.CreateWriter(anchor);
    }

    /// <summary>
    /// Gets the warnings building raised, such as a picture that could not be found.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Gets the block writer content is built with.
    /// </summary>
    public WordBlockWriter Writer => _writer;

    /// <summary>
    /// Builds one block or an array of blocks.
    /// </summary>
    /// <param name="blocks">A block object or an array of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The elements, in order.</returns>
    public async Task<List<OpenXmlElement>> BuildAsync(JsonElement blocks, CancellationToken cancellationToken)
    {
        var elements = new List<OpenXmlElement>();

        if (blocks.ValueKind == JsonValueKind.Array)
        {
            var index = 0;

            foreach (var block in blocks.EnumerateArray())
            {
                index++;

                try
                {
                    elements.AddRange(await BuildBlockAsync(block, cancellationToken));
                }
                catch (WordToolException ex)
                {
                    throw new WordToolException($"Block {index}: {ex.Message}", ex);
                }
            }
        }
        else if (blocks.ValueKind == JsonValueKind.Object)
        {
            elements.AddRange(await BuildBlockAsync(blocks, cancellationToken));
        }
        else if (blocks.ValueKind == JsonValueKind.String)
        {
            // A model sometimes sends content as Markdown text rather than blocks.
            elements.AddRange(_writer.FromRichText(RichTextParser.Parse(blocks.GetString())));
        }

        return elements;
    }

    /// <summary>
    /// Builds one block.
    /// </summary>
    /// <param name="block">The block object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The elements the block becomes.</returns>
    public async Task<List<OpenXmlElement>> BuildBlockAsync(JsonElement block, CancellationToken cancellationToken)
    {
        if (block.ValueKind == JsonValueKind.String)
        {
            return [.. _writer.FromRichText(RichTextParser.Parse(block.GetString()))];
        }

        if (block.ValueKind != JsonValueKind.Object)
        {
            throw new WordToolException("Each content block must be an object with a 'type'.");
        }

        var type = NormalizeType(WordJsonValues.GetString(block, "type"), block);
        var text = WordJsonValues.GetRawString(block, "text");
        var elements = new List<OpenXmlElement>();

        switch (type)
        {
            case "heading":
                elements.Add(Format(_writer.Heading(text ?? string.Empty, Math.Clamp(WordJsonValues.GetInt(block, "level") ?? 1, 1, 6)), block));

                break;

            case "title":
                elements.Add(Format(_writer.Paragraph(text ?? string.Empty, _writer.Style(WordStyleSheet.Title)), block));

                break;

            case "subtitle":
                elements.Add(Format(_writer.Paragraph(text ?? string.Empty, _writer.Style(WordStyleSheet.Subtitle)), block));

                break;

            case "paragraph":
                elements.Add(Format(_writer.Paragraph(text ?? string.Empty, ResolveParagraphStyle(WordJsonValues.GetString(block, "style"))), block));

                break;

            case "markdown":
                elements.AddRange(_writer.FromRichText(RichTextParser.Parse(text ?? string.Empty)));

                break;

            case "bullet_list":
            case "numbered_list":
                elements.AddRange(_writer.List(ReadItems(block), type == "numbered_list", Math.Max(1, WordJsonValues.GetInt(block, "start") ?? 1)));

                break;

            case "quote":
                elements.Add(Format(_writer.Quote(text ?? string.Empty), block));

                break;

            case "code":
                elements.AddRange(_writer.Code(text ?? string.Empty));

                break;

            case "page_break":
                elements.Add(WordBlockWriter.PageBreak());

                break;

            case "rule":
                elements.Add(_writer.Rule());

                break;

            case "table":
                elements.AddRange(await BuildTableAsync(block, cancellationToken));

                break;

            case "image":
                elements.AddRange(await BuildImageAsync(block, cancellationToken));

                break;

            case "chart":
                elements.AddRange(await BuildChartAsync(block, cancellationToken));

                break;

            case "caption":
                elements.Add(Caption(WordJsonValues.GetString(block, "label") ?? "Figure", text));

                break;

            default:
                throw new WordToolException($"Unknown block type \"{WordJsonValues.GetString(block, "type")}\". Use one of: {string.Join(", ", BlockTypes)}.");
        }

        return elements;
    }

    /// <summary>
    /// Builds a table block, from its own rows or from uploaded tabular data, with an optional caption.
    /// </summary>
    /// <param name="block">The block object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The caption, if any, and the table.</returns>
    public async Task<List<OpenXmlElement>> BuildTableAsync(JsonElement block, CancellationToken cancellationToken)
    {
        var spec = await ReadTableAsync(block, cancellationToken);

        if (spec.Columns.Count == 0 && spec.Rows.Count == 0)
        {
            throw new WordToolException("A table needs 'columns' and 'rows', or a tabular 'source'.");
        }

        var table = _writer.Table(spec);
        var caption = WordJsonValues.GetString(block, "caption");

        if (caption is null)
        {
            return [table];
        }

        var captionParagraph = Caption("Table", caption);

        return string.Equals(WordJsonValues.GetString(block, "caption_position"), "below", StringComparison.OrdinalIgnoreCase)
            ? [table, captionParagraph]
            : [captionParagraph, table];
    }

    /// <summary>
    /// Reads a table description: its columns, rows, look, and an optional tabular source.
    /// </summary>
    /// <param name="block">The table object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The table description.</returns>
    public async Task<WordTableSpec> ReadTableAsync(JsonElement block, CancellationToken cancellationToken)
    {
        var spec = new WordTableSpec
        {
            HeaderRow = WordJsonValues.GetBoolean(block, "header_row") ?? true,
            RepeatHeaderRow = WordJsonValues.GetBoolean(block, "repeat_header") ?? true,
            Style = WordJsonValues.GetString(block, "table_style") ?? WordJsonValues.GetString(block, "style"),
            Banded = WordJsonValues.GetBoolean(block, "banded"),
            HeaderFill = WordJsonValues.GetString(block, "header_fill"),
            HeaderTextColor = WordJsonValues.GetString(block, "header_text_color"),
            BorderColor = WordJsonValues.GetString(block, "border_color"),
            BandFill = WordJsonValues.GetString(block, "band_fill"),
            FontSize = WordJsonValues.GetDouble(block, "font_size"),
            Width = WordJsonValues.GetString(block, "width"),
            Alignment = WordJsonValues.GetString(block, "alignment"),
            BoldFirstColumn = WordJsonValues.GetBoolean(block, "bold_first_column") ?? false,
        };

        if (WordJsonValues.TryGet(block, "columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
        {
            foreach (var column in columns.EnumerateArray())
            {
                spec.Columns.Add(ReadColumn(column));
            }
        }

        if (WordJsonValues.TryGet(block, "source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            var result = await WordTabularSource.QueryAsync(source, _tools.Services, cancellationToken);

            for (var index = 0; index < result.Headers.Count; index++)
            {
                if (index >= spec.Columns.Count)
                {
                    spec.Columns.Add(new WordTableColumn());
                }

                var column = spec.Columns[index];

                column.Header ??= result.Headers[index];
                column.Format ??= result.Formatting?.FindColumn(result.Headers[index]);
            }

            foreach (var row in result.Rows)
            {
                spec.Rows.Add([.. row.Select(value => new WordTableCell { Value = value is DBNull ? string.Empty : value ?? string.Empty })]);
            }

            if (result.Truncated)
            {
                Warnings.Add($"The table holds the first {result.Rows.Count} rows of {result.Description}; pass a larger source.max_rows or aggregate in the SQL to include more.");
            }

            return spec;
        }

        if (WordJsonValues.TryGet(block, "rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                spec.Rows.Add(ReadRow(row, spec.Columns.Count));
            }
        }

        return spec;
    }

    /// <summary>
    /// Reads a chart description, from its own data or from uploaded tabular data.
    /// </summary>
    /// <param name="block">The chart object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chart description.</returns>
    public async Task<WordChartSpec> ReadChartAsync(JsonElement block, CancellationToken cancellationToken)
    {
        var spec = new WordChartSpec
        {
            Type = WordChartSpec.Normalize(WordJsonValues.GetString(block, "chart_type") ?? WordJsonValues.GetString(block, "kind")),
            Title = WordJsonValues.GetString(block, "title"),
            Labels = WordJsonValues.GetStrings(block, "labels"),
            Legend = WordJsonValues.GetString(block, "legend") ?? "bottom",
            DataLabels = WordJsonValues.GetBoolean(block, "data_labels") ?? false,
            XAxisTitle = WordJsonValues.GetString(block, "x_axis_title"),
            YAxisTitle = WordJsonValues.GetString(block, "y_axis_title"),
            NumberFormat = WordJsonValues.GetString(block, "number_format"),
            Colors = WordJsonValues.GetStrings(block, "colors", splitCommas: true),
            Smooth = WordJsonValues.GetBoolean(block, "smooth") ?? false,
        };

        if (spec.Colors.Count == 0 && spec.Kind is not "pie" and not "doughnut")
        {
            spec.Colors.Add(_edit.Design.AccentColor);
            spec.Colors.AddRange(WordChartWriter.Palette.Skip(1));
        }

        if (WordJsonValues.TryGet(block, "source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            await ReadChartSourceAsync(spec, source, cancellationToken);

            return spec;
        }

        if (WordJsonValues.TryGet(block, "series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in series.EnumerateArray())
            {
                spec.Series.Add(new WordChartSeries
                {
                    Name = WordJsonValues.GetString(item, "name"),
                    Values = ReadNumbers(item, "values"),
                    XValues = ReadNumbers(item, "x_values"),
                    Color = WordJsonValues.GetString(item, "color"),
                });
            }
        }
        else if (WordJsonValues.TryGet(block, "values", out _))
        {
            spec.Series.Add(new WordChartSeries { Name = spec.Title, Values = ReadNumbers(block, "values") });
        }

        return spec;
    }

    /// <summary>
    /// Builds a caption paragraph, numbered after the captions of the same label already in the document.
    /// </summary>
    /// <param name="label">The label, such as <c>Figure</c> or <c>Table</c>.</param>
    /// <param name="text">The caption text.</param>
    /// <returns>The paragraph.</returns>
    public Paragraph Caption(string label, string text)
    {
        var normalized = WordCaptions.NormalizeLabel(label);
        var count = Fields.WordFieldScanner.Scan(_edit.Package.Body)
            .Count(field => field.Type == "SEQ" &&
                string.Equals(Fields.WordFieldScanner.ArgumentOf(field.Instruction), normalized, StringComparison.OrdinalIgnoreCase) &&
                !Fields.WordFieldScanner.HasSwitch(field.Instruction, 'c'));

        _bookmarks ??= WordBookmarks.For(_edit.Package);

        var name = _bookmarks.HiddenName("_Ref");

        return WordCaptions.Create(_writer, normalized, text, count + 1, name, _bookmarks.NextId());
    }

    /// <summary>
    /// Returns the next free drawing id.
    /// </summary>
    /// <returns>The id.</returns>
    public uint NextDrawingId()
    {
        _drawingIds ??= WordDrawingIds.For(_edit.Package);

        return _drawingIds.Next();
    }

    /// <summary>
    /// Reads the size and placement of a picture from a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="textWidth">The text width in points, what a percentage refers to.</param>
    /// <returns>The options.</returns>
    public static WordImageOptions ReadImageOptions(JsonElement block, double textWidth)
    {
        double[] crop = null;

        if (WordJsonValues.TryGet(block, "crop", out var cropValue) && cropValue.ValueKind == JsonValueKind.Object)
        {
            crop =
            [
                WordJsonValues.GetDouble(cropValue, "left") ?? 0,
                WordJsonValues.GetDouble(cropValue, "top") ?? 0,
                WordJsonValues.GetDouble(cropValue, "right") ?? 0,
                WordJsonValues.GetDouble(cropValue, "bottom") ?? 0,
            ];
        }

        return new WordImageOptions
        {
            Width = WordFormatReader.ReadLength(block, "width", textWidth),
            Height = WordFormatReader.ReadLength(block, "height", textWidth),
            AltText = WordJsonValues.GetString(block, "alt_text"),
            Name = WordJsonValues.GetString(block, "name"),
            Alignment = WordJsonValues.GetString(block, "alignment") ?? "center",
            Wrap = WordJsonValues.GetString(block, "wrap"),
            OffsetX = WordFormatReader.ReadLength(block, "offset_x", textWidth),
            OffsetY = WordFormatReader.ReadLength(block, "offset_y", textWidth),
            Crop = crop,
            BorderColor = WordJsonValues.GetString(block, "border_color"),
            BorderWidth = WordJsonValues.GetDouble(block, "border_width"),
        };
    }

    private async Task<List<OpenXmlElement>> BuildImageAsync(JsonElement block, CancellationToken cancellationToken)
    {
        var source = WordJsonValues.GetString(block, "source") ?? WordJsonValues.GetString(block, "image");

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new WordToolException("An image needs a 'source': an uploaded image's file name or id, a [fig:N] marker, a figure:{documentId}/{figureId} reference, or a data: URI.");
        }

        var image = await _tools.ResolveImageAsync(source, cancellationToken)
            ?? throw new WordToolException($"No picture was found for \"{source}\". Name an uploaded image (PNG, JPEG, GIF, BMP or TIFF), a [fig:N] marker from this turn, or a figure reference.");

        var textWidth = WordUnits.FromTwips(_writer.TextWidthTwips);
        var options = ReadImageOptions(block, textWidth);

        if (string.IsNullOrWhiteSpace(options.AltText))
        {
            Warnings.Add($"The picture from \"{source}\" has no alt_text; screen readers announce it as an unlabelled image. Add alt_text describing it.");
        }

        var paragraph = WordImageWriter.CreateParagraph(_edit.Package.MainPart, image, options, NextDrawingId(), textWidth);
        var caption = WordJsonValues.GetString(block, "caption");

        if (caption is null)
        {
            return [paragraph];
        }

        if (string.Equals(WordJsonValues.GetString(block, "caption_position"), "above", StringComparison.OrdinalIgnoreCase))
        {
            var above = Caption("Figure", caption);

            above.ParagraphProperties.KeepNext = new KeepNext();

            return [above, paragraph];
        }

        // A figure keeps with the caption under it, so the two never land on different pages.
        (paragraph.ParagraphProperties ??= new ParagraphProperties()).KeepNext = new KeepNext();

        return [paragraph, Caption("Figure", caption)];
    }

    private async Task<List<OpenXmlElement>> BuildChartAsync(JsonElement block, CancellationToken cancellationToken)
    {
        var spec = await ReadChartAsync(block, cancellationToken);
        var textWidth = WordUnits.FromTwips(_writer.TextWidthTwips);
        // A chart is at least a quarter inch each way, no wider than the text and no taller than the largest page
        // Word allows (22 inches).
        var width = Math.Clamp(WordFormatReader.ReadLength(block, "width", textWidth) ?? textWidth, MinChartSize, Math.Max(MinChartSize, textWidth));
        var height = Math.Clamp(WordFormatReader.ReadLength(block, "height", textWidth) ?? Math.Round(width * 0.6), MinChartSize, MaxChartHeight);
        var paragraph = WordChartWriter.CreateParagraph(_edit.Package.MainPart, spec, NextDrawingId(), width, height, WordJsonValues.GetString(block, "alt_text"));
        var caption = WordJsonValues.GetString(block, "caption");

        if (caption is null)
        {
            return [paragraph];
        }

        paragraph.ParagraphProperties.KeepNext = new KeepNext();

        return [paragraph, Caption("Figure", caption)];
    }

    private async Task ReadChartSourceAsync(WordChartSpec spec, JsonElement source, CancellationToken cancellationToken)
    {
        var result = await WordTabularSource.QueryAsync(source, _tools.Services, cancellationToken);

        if (result.Headers.Count == 0 || result.Rows.Count == 0)
        {
            throw new WordToolException("The chart's data query returned no rows.");
        }

        var labelColumn = WordJsonValues.GetString(source, "label_column");
        var labelIndex = string.IsNullOrWhiteSpace(labelColumn) ? 0 : FindHeader(result.Headers, labelColumn);

        if (labelIndex < 0)
        {
            throw new WordToolException($"The chart names the label column \"{labelColumn}\", which the query does not return. It returns: {string.Join(", ", result.Headers)}.");
        }

        var valueIndexes = new List<int>();

        foreach (var name in WordJsonValues.GetStrings(source, "value_columns", splitCommas: true))
        {
            var index = FindHeader(result.Headers, name);

            if (index < 0)
            {
                throw new WordToolException($"The chart names the value column \"{name}\", which the query does not return. It returns: {string.Join(", ", result.Headers)}.");
            }

            valueIndexes.Add(index);
        }

        if (valueIndexes.Count == 0)
        {
            for (var index = 0; index < result.Headers.Count; index++)
            {
                if (index != labelIndex && result.Rows.All(row => row[index] is null or DBNull || WordTabularSource.ToNumber(row[index]) is not null))
                {
                    valueIndexes.Add(index);
                }
            }
        }

        if (valueIndexes.Count == 0)
        {
            throw new WordToolException("The chart's query returns no numeric column to plot; name one in source.value_columns.");
        }

        spec.Labels = [.. result.Rows.Select(row => Convert.ToString(row[labelIndex] is DBNull ? string.Empty : row[labelIndex], CultureInfo.InvariantCulture) ?? string.Empty)];
        spec.Series = [.. valueIndexes.Select(index => new WordChartSeries
        {
            Name = result.Headers[index],
            Values = [.. result.Rows.Select(row => WordTabularSource.ToNumber(row[index]))],
        })];

        if (string.IsNullOrWhiteSpace(spec.NumberFormat) && result.Formatting?.FindColumn(result.Headers[valueIndexes[0]]) is { } format)
        {
            var code = SpreadsheetNumberFormatCode.Resolve(format);

            if (!string.IsNullOrWhiteSpace(code) && !string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
            {
                spec.NumberFormat = code;
            }
        }
    }

    private static int FindHeader(IReadOnlyList<string> headers, string name)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (SpreadsheetFormatting.NameMatches(headers[index], name))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<double?> ReadNumbers(JsonElement element, string name)
    {
        if (!WordJsonValues.TryGet(element, name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. values.EnumerateArray().Select(WordJsonValues.ReadDouble)];
    }

    private static Paragraph Format(Paragraph paragraph, JsonElement block)
    {
        var alignment = WordJsonValues.GetString(block, "alignment");
        var paragraphFormat = WordJsonValues.TryGet(block, "paragraph_format", out var paragraphElement) ? WordFormatReader.ReadParagraph(paragraphElement) : null;
        var runFormat = WordJsonValues.TryGet(block, "format", out var runElement) ? WordFormatReader.ReadRun(runElement) : null;

        if (alignment is not null)
        {
            paragraphFormat ??= new WordParagraphFormat();
            paragraphFormat.Alignment ??= alignment;
        }

        if (paragraphFormat is not null)
        {
            paragraphFormat.ApplyTo(paragraph.ParagraphProperties ??= new ParagraphProperties());
        }

        if (runFormat is { IsEmpty: false })
        {
            foreach (var run in paragraph.Descendants<Run>())
            {
                runFormat.ApplyTo(run.RunProperties ??= new RunProperties());
            }
        }

        return paragraph;
    }

    private string ResolveParagraphStyle(string style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        var found = WordStyleSheet.Find(_edit.Package.MainPart, style, StyleValues.Paragraph);

        if (found is not null)
        {
            return found;
        }

        // A standard style the document lacks is added; any other name is reported, not invented.
        var standard = style.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (standard.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || standard is "Quote" or "Caption" or "NoSpacing" or "Title" or "Subtitle" or "CodeBlock" or "ListParagraph")
        {
            return _writer.Style(char.ToUpperInvariant(standard[0]) + standard[1..]);
        }

        Warnings.Add($"The document has no paragraph style \"{style}\"; the paragraph was left in body text. manage_word_styles lists and creates styles.");

        return null;
    }

    private static List<(string Text, int Level)> ReadItems(JsonElement block)
    {
        var items = new List<(string Text, int Level)>();

        if (!WordJsonValues.TryGet(block, "items", out var array))
        {
            var text = WordJsonValues.GetRawString(block, "text");

            if (!string.IsNullOrWhiteSpace(text))
            {
                items.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => (line.Trim().TrimStart('-', '*', '•').Trim(), 0)));
            }

            return items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        Collect(array, 0, items);

        return items;
    }

    private static void Collect(JsonElement array, int level, List<(string Text, int Level)> items)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                items.Add((item.GetString(), level));

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var itemLevel = WordJsonValues.GetInt(item, "level") ?? level;

            items.Add((WordJsonValues.GetRawString(item, "text") ?? string.Empty, Math.Clamp(itemLevel, 0, 8)));

            if (WordJsonValues.TryGet(item, "items", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                Collect(children, Math.Min(itemLevel + 1, 8), items);
            }
        }
    }

    private static WordTableColumn ReadColumn(JsonElement column)
    {
        if (column.ValueKind == JsonValueKind.String)
        {
            return new WordTableColumn { Header = column.GetString() };
        }

        if (column.ValueKind != JsonValueKind.Object)
        {
            return new WordTableColumn { Header = column.GetRawText() };
        }

        return new WordTableColumn
        {
            Header = WordJsonValues.GetString(column, "header") ?? WordJsonValues.GetString(column, "name") ?? string.Empty,
            Width = WordJsonValues.GetString(column, "width"),
            Alignment = WordJsonValues.GetString(column, "alignment"),
            Format = ReadColumnFormat(column),
        };
    }

    /// <summary>
    /// Reads how a column's values are presented: <c>currency</c>, <c>accounting</c>, <c>percent</c>,
    /// <c>number</c>, <c>integer</c>, <c>date</c>, <c>datetime</c>, <c>text</c>, or a spreadsheet format code.
    /// </summary>
    /// <param name="column">The column object.</param>
    /// <returns>The format, or <see langword="null"/> when none is given.</returns>
    public static SpreadsheetColumnFormat ReadColumnFormat(JsonElement column)
    {
        var format = WordJsonValues.GetString(column, "format");

        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        var decimals = WordJsonValues.GetInt(column, "decimals");
        var result = new SpreadsheetColumnFormat
        {
            Column = WordJsonValues.GetString(column, "header"),
            Decimals = decimals,
            CurrencySymbol = WordJsonValues.GetString(column, "currency_symbol"),
        };

        switch (format.Trim().ToLowerInvariant())
        {
            case "currency":
            case "money":
                result.NumberFormat = SpreadsheetNumberFormat.Currency;
                result.Decimals ??= 2;

                break;

            case "accounting":
                result.NumberFormat = SpreadsheetNumberFormat.Accounting;
                result.Decimals ??= 2;

                break;

            case "percent":
            case "percentage":
                result.NumberFormat = SpreadsheetNumberFormat.Percent;
                result.Decimals ??= 1;

                break;

            case "number":
            case "decimal":
                result.NumberFormat = SpreadsheetNumberFormat.Number;
                result.Decimals ??= 2;

                break;

            case "integer":
            case "whole":
                result.NumberFormat = SpreadsheetNumberFormat.Number;
                result.Decimals = 0;

                break;

            case "date":
                result.NumberFormat = SpreadsheetNumberFormat.Date;

                break;

            case "datetime":
                result.NumberFormat = SpreadsheetNumberFormat.DateTime;

                break;

            case "text":
                result.NumberFormat = SpreadsheetNumberFormat.Text;

                break;

            default:
                result.FormatCode = format.Trim();

                break;
        }

        return result;
    }

    private static List<WordTableCell> ReadRow(JsonElement row, int columnCount)
    {
        if (row.ValueKind == JsonValueKind.Object && WordJsonValues.TryGet(row, "cells", out var cells))
        {
            row = cells;
        }

        if (row.ValueKind != JsonValueKind.Array)
        {
            return [new WordTableCell { Value = WordJsonValues.ReadScalar(row) }];
        }

        var result = new List<WordTableCell>(Math.Max(columnCount, 1));

        foreach (var cell in row.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.Object)
            {
                result.Add(new WordTableCell { Value = WordJsonValues.ReadScalar(cell) });

                continue;
            }

            var value = WordJsonValues.TryGet(cell, "value", out var raw) ? WordJsonValues.ReadScalar(raw) : null;

            result.Add(new WordTableCell
            {
                Text = value is null ? WordJsonValues.GetRawString(cell, "text") ?? string.Empty : null,
                Value = value,
                Bold = WordJsonValues.GetBoolean(cell, "bold"),
                Italic = WordJsonValues.GetBoolean(cell, "italic"),
                Color = WordJsonValues.GetString(cell, "color"),
                Fill = WordJsonValues.GetString(cell, "fill") ?? WordJsonValues.GetString(cell, "shading"),
                Alignment = WordJsonValues.GetString(cell, "alignment"),
                VerticalAlignment = WordJsonValues.GetString(cell, "vertical_alignment"),
                ColumnSpan = Math.Max(1, WordJsonValues.GetInt(cell, "colspan") ?? WordJsonValues.GetInt(cell, "column_span") ?? 1),
                RowSpan = Math.Max(1, WordJsonValues.GetInt(cell, "rowspan") ?? WordJsonValues.GetInt(cell, "row_span") ?? 1),
            });
        }

        return result;
    }

    private static string NormalizeType(string type, JsonElement block)
    {
        var text = (type ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        return text switch
        {
            "" when WordJsonValues.TryGet(block, "items", out _) => "bullet_list",
            "" when WordJsonValues.TryGet(block, "rows", out _) || WordJsonValues.TryGet(block, "columns", out _) => "table",
            "" => "paragraph",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
            "text" or "body" or "p" => "paragraph",
            "list" or "bullets" or "bulleted_list" or "unordered_list" => "bullet_list",
            "numbers" or "ordered_list" or "numbered" => "numbered_list",
            "blockquote" or "block_quote" => "quote",
            "code_block" or "pre" => "code",
            "picture" or "figure" or "photo" or "logo" => "image",
            "graph" => "chart",
            "pagebreak" or "break" => "page_break",
            "horizontal_rule" or "divider" or "hr" or "line" => "rule",
            "md" => "markdown",
            _ => text,
        };
    }
}
