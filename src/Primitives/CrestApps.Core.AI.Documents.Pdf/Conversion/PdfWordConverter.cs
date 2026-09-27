using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;
using DrawingWord = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Converts a Word document into the blocks of a composed PDF: headings from heading styles and outline
/// levels, paragraphs with their bold, italic, struck-through text and links, bulleted and numbered lists,
/// tables, pictures and page breaks.
/// </summary>
/// <remarks>
/// The result is the document's content set in the PDF's own theme, not a copy of its page design; text
/// boxes, charts, equations, footnotes and the Word header and footer are named as not carried over.
/// </remarks>
internal sealed partial class PdfWordConverter
{
    private const double PointsPerEmu = 1d / 12700;

    private readonly WordprocessingDocument _document;
    private readonly PdfImageStore _images;
    private readonly PdfConversionResult _result = new();
    private readonly PdfConversionBlocks _blocks;
    private readonly Dictionary<string, Style> _styles;
    private int _textBoxes;
    private int _charts;
    private int _equations;

    private PdfWordConverter(WordprocessingDocument document, PdfImageStore images)
    {
        _document = document;
        _images = images;
        _blocks = new PdfConversionBlocks(_result.Blocks);
        _styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(style => style.StyleId?.Value is not null)
            .GroupBy(style => style.StyleId.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a Word document.
    /// </summary>
    /// <param name="bytes">The .docx file.</param>
    /// <param name="images">Where the document's pictures are kept for the PDF.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The blocks.</returns>
    public static async Task<PdfConversionResult> ConvertAsync(byte[] bytes, PdfImageStore images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(images);

        using var stream = new MemoryStream(bytes, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);

        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new Workspace.PdfToolException("The Word document has no body to convert.");

        var converter = new PdfWordConverter(document, images);

        converter._result.Title = document.PackageProperties.Title;

        await converter.WalkAsync(body.ChildElements, cancellationToken);
        converter._blocks.FlushList();
        converter.Finish(body);

        return converter._result;
    }

    private async Task WalkAsync(IEnumerable<OpenXmlElement> elements, CancellationToken cancellationToken)
    {
        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                    await ParagraphAsync(paragraph, cancellationToken);

                    break;

                case Table table:
                    _blocks.Table(ReadTable(table));

                    break;

                case SdtBlock content:
                    await WalkAsync(content.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>(), cancellationToken);

                    break;

                case CustomXmlBlock custom:
                    await WalkAsync(custom.ChildElements, cancellationToken);

                    break;
            }
        }
    }

    private async Task ParagraphAsync(Paragraph paragraph, CancellationToken cancellationToken)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var text = new PdfInlineMarkdown();
        var pictures = new List<(string Source, double? Width, string Alt)>();
        var pageBreakBefore = properties?.PageBreakBefore is { } before && (before.Val is null || before.Val.Value);
        var pageBreakAfter = false;

        if (pageBreakBefore)
        {
            _blocks.PageBreak();
        }

        foreach (var run in paragraph.Descendants<Run>())
        {
            // Deleted revisions, text boxes and field instructions are not what the page shows as this paragraph.
            if (run.Ancestors<DeletedRun>().Any() || run.Ancestors<TextBoxContent>().Any())
            {
                continue;
            }

            var runProperties = run.RunProperties;
            var bold = IsOn(runProperties?.Bold);
            var italic = IsOn(runProperties?.Italic);
            var strike = IsOn(runProperties?.Strike) || IsOn(runProperties?.DoubleStrike);
            var link = LinkOf(run);

            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text value:
                        text.Append(value.Text, bold, italic, strike, link: link);

                        break;

                    case TabChar:
                        text.Append(" ", bold, italic, strike, link: link);

                        break;

                    case Break lineBreak when lineBreak.Type?.Value == BreakValues.Page:
                        pageBreakAfter = true;

                        break;

                    case Break or CarriageReturn:
                        text.Append("\n", bold, italic, strike, link: link);

                        break;

                    case NoBreakHyphen:
                        text.Append("-", bold, italic, strike, link: link);

                        break;

                    case DocumentFormat.OpenXml.Wordprocessing.Drawing drawing:
                        await DrawingAsync(drawing, pictures, cancellationToken);

                        break;

                    case Picture legacy:
                        await LegacyPictureAsync(legacy, pictures, cancellationToken);

                        break;
                }
            }
        }

        _textBoxes += paragraph.Descendants<TextBoxContent>().Count();
        _equations += paragraph.Descendants().Count(element => element.LocalName == "oMath");

        var markdown = text.ToString();
        var heading = HeadingLevel(properties, styleId);
        var numbering = Numbering(properties, styleId);

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            if (heading is { } level)
            {
                if (level == 0)
                {
                    _result.Title ??= Plain(markdown);
                }

                _blocks.Heading(markdown, Math.Max(level, 1));
            }
            else if (numbering is { } item)
            {
                _blocks.ListItem(markdown, item.Level, item.Ordered);
            }
            else
            {
                _blocks.Paragraph(markdown, Alignment(properties));
            }
        }

        foreach (var (source, width, alt) in pictures)
        {
            _blocks.Image(source, width, alt);
        }

        if (pageBreakAfter)
        {
            _blocks.PageBreak();
        }
    }

    private async Task DrawingAsync(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing, List<(string, double?, string)> pictures, CancellationToken cancellationToken)
    {
        if (drawing.Descendants().Any(element => element.LocalName == "chart"))
        {
            _charts++;

            return;
        }

        var blip = drawing.Descendants<Drawing.Blip>().FirstOrDefault();

        if (blip?.Embed?.Value is not { } id)
        {
            return;
        }

        var extent = drawing.Descendants<DrawingWord.Extent>().FirstOrDefault();
        var properties = drawing.Descendants<DrawingWord.DocProperties>().FirstOrDefault();
        var width = extent?.Cx?.Value is long cx ? cx * PointsPerEmu : (double?)null;
        var source = await StoreAsync(id, properties?.Description?.Value ?? properties?.Title?.Value, cancellationToken);

        if (source is not null)
        {
            pictures.Add((source, width, properties?.Description?.Value));
        }
    }

    private async Task LegacyPictureAsync(Picture picture, List<(string, double?, string)> pictures, CancellationToken cancellationToken)
    {
        var data = picture.Descendants<DocumentFormat.OpenXml.Vml.ImageData>().FirstOrDefault();

        if (data?.RelationshipId?.Value is { } id && await StoreAsync(id, data.Title?.Value, cancellationToken) is { } source)
        {
            pictures.Add((source, null, data.Title?.Value));
        }
    }

    private async Task<string> StoreAsync(string relationshipId, string description, CancellationToken cancellationToken)
    {
        if (_document.MainDocumentPart?.GetPartById(relationshipId) is not ImagePart part)
        {
            return null;
        }

        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return await _images.StoreAsync(buffer.ToArray(), part.ContentType, Path.GetFileName(part.Uri.OriginalString), description, cancellationToken);
    }

    private static List<List<string>> ReadTable(Table table)
    {
        var rows = new List<List<string>>();

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();

            foreach (var cell in row.Elements<TableCell>())
            {
                var text = string.Join(" ", cell.Descendants<Paragraph>()
                    .Select(paragraph => string.Concat(paragraph.Descendants<Text>().Where(value => !value.Ancestors<DeletedRun>().Any()).Select(value => value.Text)).Trim())
                    .Where(value => value.Length > 0));

                cells.Add(text);

                // A merged cell spans several grid columns; the ones it covers stay empty.
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;

                for (var index = 1; index < span; index++)
                {
                    cells.Add(string.Empty);
                }
            }

            rows.Add(cells);
        }

        return rows;
    }

    private int? HeadingLevel(ParagraphProperties properties, string styleId)
    {
        if (properties?.OutlineLevel?.Val?.Value is int outline && outline < 9)
        {
            return outline + 1;
        }

        // A file written by other software may name the built-in style without defining it.
        if (Find(styleId) is null && styleId is not null)
        {
            var named = HeadingName().Match(styleId);

            if (named.Success)
            {
                return int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            return string.Equals(styleId, "Title", StringComparison.OrdinalIgnoreCase) ? 0 : null;
        }

        for (var (style, depth) = (Find(styleId), 0); style is not null && depth < 8; style = Find(style.BasedOn?.Val?.Value), depth++)
        {
            var name = style.StyleName?.Val?.Value ?? style.StyleId?.Value ?? string.Empty;

            if (string.Equals(name, "Title", StringComparison.OrdinalIgnoreCase) || string.Equals(style.StyleId?.Value, "Title", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var match = HeadingName().Match(name);

            if (!match.Success)
            {
                match = HeadingName().Match(style.StyleId?.Value ?? string.Empty);
            }

            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int styleOutline && styleOutline < 9)
            {
                return styleOutline + 1;
            }
        }

        return null;
    }

    private (int Level, bool Ordered)? Numbering(ParagraphProperties properties, string styleId)
    {
        var numbering = properties?.NumberingProperties;

        for (var (style, depth) = (Find(styleId), 0); numbering?.NumberingId is null && style is not null && depth < 8; style = Find(style.BasedOn?.Val?.Value), depth++)
        {
            numbering = style.StyleParagraphProperties?.NumberingProperties;
        }

        var numberId = numbering?.NumberingId?.Val?.Value;

        if (numberId is null or 0)
        {
            return null;
        }

        var level = numbering.NumberingLevelReference?.Val?.Value ?? 0;
        var definitions = _document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
        var instance = definitions?.Elements<NumberingInstance>().FirstOrDefault(candidate => candidate.NumberID?.Value == numberId);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        var definition = definitions?.Elements<AbstractNum>().FirstOrDefault(candidate => candidate.AbstractNumberId?.Value == abstractId);
        var format = definition?.Elements<Level>().FirstOrDefault(candidate => candidate.LevelIndex?.Value == level)?.NumberingFormat?.Val?.Value;

        if (format == NumberFormatValues.None)
        {
            return null;
        }

        return (level, format is not null && format != NumberFormatValues.Bullet);
    }

    private Style Find(string styleId)
    {
        return styleId is not null && _styles.TryGetValue(styleId, out var style) ? style : null;
    }

    private string LinkOf(Run run)
    {
        var hyperlink = run.Ancestors<Hyperlink>().FirstOrDefault();

        if (hyperlink?.Id?.Value is not { } id)
        {
            return null;
        }

        return _document.MainDocumentPart?.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == id)?.Uri?.OriginalString;
    }

    private void Finish(Body body)
    {
        var size = body.Elements<SectionProperties>().LastOrDefault()?.GetFirstChild<PageSize>();

        _result.Landscape = size?.Orient?.Value == PageOrientationValues.Landscape ||
            (size?.Width?.Value is uint width && size.Height?.Value is uint height && width > height);

        if (_textBoxes > 0)
        {
            _result.Warnings.Add($"{_textBoxes} text box(es) were not carried over.");
        }

        if (_charts > 0)
        {
            _result.Warnings.Add($"{_charts} chart(s) were not carried over; recreate them with add_pdf_content (type chart).");
        }

        if (_equations > 0)
        {
            _result.Warnings.Add($"{_equations} equation(s) were not carried over.");
        }

        if (_document.MainDocumentPart?.FootnotesPart?.Footnotes?.Elements<Footnote>().Count(note => note.Id?.Value > 0) is > 0)
        {
            _result.Warnings.Add("Footnotes were not carried over.");
        }

        if (_document.MainDocumentPart?.HeaderParts.Any() == true || _document.MainDocumentPart?.FooterParts.Any() == true)
        {
            _result.Warnings.Add("The Word header and footer were not carried over; set running heads and page numbers with format_pdf.");
        }

        _result.Warnings.AddRange(_images.Warnings);
    }

    private static bool IsOn(OnOffType value)
    {
        return value is not null && (value.Val is null || value.Val.Value);
    }

    private static string Alignment(ParagraphProperties properties)
    {
        var value = properties?.Justification?.Val?.Value;

        if (value == JustificationValues.Center)
        {
            return "center";
        }

        if (value == JustificationValues.Right || value == JustificationValues.End)
        {
            return "right";
        }

        return value == JustificationValues.Both ? "justify" : null;
    }

    private static string Plain(string markdown)
    {
        var builder = new StringBuilder(markdown.Length);

        for (var index = 0; index < markdown.Length; index++)
        {
            var character = markdown[index];

            if (character == '\\' && index + 1 < markdown.Length)
            {
                builder.Append(markdown[++index]);
            }
            else if (character is not ('*' or '~' or '`'))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingName();
}
