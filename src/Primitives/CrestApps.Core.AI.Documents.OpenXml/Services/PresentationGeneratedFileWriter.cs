using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as a PowerPoint deck (<c>.pptx</c>): a title slide, then one
/// slide per heading with its bullets, paragraphs and tables, and quoted <c>Notes:</c> lines as speaker notes.
/// </summary>
/// <remarks>
/// This is the plain route from Markdown to a deck, for code that produces a file in one step. Decks that are
/// designed, previewed and revised belong to the presentation agent, which builds on the same engine.
/// </remarks>
public sealed class PresentationGeneratedFileWriter : IGeneratedFileWriter
{
    private const int MaxBodyParagraphs = 8;
    private const int MaxTableRows = 12;

    private readonly IPresentationEngine _engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationGeneratedFileWriter"/> class.
    /// </summary>
    /// <param name="engine">The presentation engine.</param>
    public PresentationGeneratedFileWriter(IPresentationEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Writes the content as a deck to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        var text = content.Text ?? string.Empty;

        if (HtmlToMarkupConverter.LooksLikeHtml(text))
        {
            text = HtmlToMarkupConverter.Convert(text);
        }

        var blocks = RichTextParser.Parse(text);
        var slides = Plan(blocks, content.Title, out var deckTitle, out var subtitle);

        foreach (var sheet in content.GetSheets().Where(sheet => sheet.HasTable))
        {
            slides.Add(TableSlide(sheet.Name ?? deckTitle, sheet.Header, sheet.Rows));
        }

        var edits = new List<PresentationEdit>
        {
            new AddSlideEdit { Layout = "title", Title = deckTitle ?? "Presentation", Subtitle = subtitle },
        };

        edits.AddRange(slides);

        var package = await _engine.CreateAsync(new PresentationCreateOptions { Title = deckTitle }, cancellationToken);
        var result = await _engine.EditAsync(package, edits, new PresentationEditContext(), cancellationToken);

        await destination.WriteAsync(result.Package, cancellationToken);
    }

    private static List<AddSlideEdit> Plan(IReadOnlyList<RichTextBlock> blocks, string title, out string deckTitle, out string subtitle)
    {
        var headings = blocks.Where(block => block.Kind == RichTextBlockKind.Heading).ToList();
        var start = 0;
        deckTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        subtitle = null;

        // A lone top-level heading at the start names the deck, and a short paragraph under it is its subtitle.
        if (blocks.Count > 0 && blocks[0].Kind == RichTextBlockKind.Heading && headings.Count(heading => heading.Level == blocks[0].Level) == 1)
        {
            deckTitle ??= Plain(blocks[0]);
            start = 1;

            if (blocks.Count > 1 && blocks[1].Kind == RichTextBlockKind.Paragraph && Plain(blocks[1]).Length <= 150)
            {
                subtitle = Plain(blocks[1]);
                start = 2;
            }
        }

        var slideLevel = headings.Skip(start > 0 ? 1 : 0).Select(heading => heading.Level).DefaultIfEmpty(0).Min();
        var slides = new List<AddSlideEdit>();
        AddSlideEdit current = null;

        for (var index = start; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if ((block.Kind == RichTextBlockKind.Heading && block.Level <= slideLevel) || block.Kind == RichTextBlockKind.HorizontalRule)
            {
                current = new AddSlideEdit { Title = block.Kind == RichTextBlockKind.Heading ? Plain(block) : current?.Title, Body = [] };
                slides.Add(current);

                continue;
            }

            if (current is null || (current.Body.Count >= MaxBodyParagraphs && block.Kind is not (RichTextBlockKind.Quote or RichTextBlockKind.Table)))
            {
                current = new AddSlideEdit { Title = current?.Title is { } previous ? previous + " (continued)" : deckTitle, Body = [] };
                slides.Add(current);
            }

            switch (block.Kind)
            {
                case RichTextBlockKind.Heading:
                    current.Body.Add(Paragraph(block, bullet: "none", new PresentationTextStyle { Bold = true }));
                    break;

                case RichTextBlockKind.BulletItem:
                    current.Body.Add(Paragraph(block, bullet: "auto", null));
                    break;

                case RichTextBlockKind.NumberedItem:
                    current.Body.Add(Paragraph(block, bullet: "number", null));
                    break;

                case RichTextBlockKind.Quote when Plain(block) is var quote && (quote.StartsWith("Notes:", StringComparison.OrdinalIgnoreCase) || quote.StartsWith("Speaker notes:", StringComparison.OrdinalIgnoreCase)):
                    current.Notes = string.IsNullOrEmpty(current.Notes) ? quote[(quote.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim() : current.Notes + "\n" + quote;
                    break;

                case RichTextBlockKind.Quote:
                    current.Body.Add(Paragraph(block, bullet: "none", new PresentationTextStyle { Italic = true }));
                    break;

                case RichTextBlockKind.Code:
                    current.Body.Add(new PresentationParagraphSpec { Bullet = "none", Runs = [new PresentationRunSpec { Text = block.Text ?? Plain(block), Style = new PresentationTextStyle { Font = "Consolas" } }] });
                    break;

                case RichTextBlockKind.Table when block.Table is { } table:
                    current.Elements.Add(TableElement(table.Header.Cells.Select(Plain).ToList(), table.Rows.Select(row => (IReadOnlyList<string>)row.Cells.Select(Plain).ToList()).ToList()));
                    break;

                default:
                    current.Body.Add(Paragraph(block, bullet: "none", null));
                    break;
            }
        }

        foreach (var slide in slides)
        {
            if (slide.Body is { Count: 0 })
            {
                slide.Body = null;
                slide.Layout = slide.Elements.Count > 0 ? "title_only" : "section_header";
            }
        }

        return slides;
    }

    private static AddSlideEdit TableSlide(string title, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        return new AddSlideEdit
        {
            Layout = "title_only",
            Title = title,
            Elements = [TableElement(header, rows)],
        };
    }

    private static PresentationElementSpec TableElement(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new PresentationTableSpec { HeaderRow = header.Count > 0 };

        if (header.Count > 0)
        {
            table.Rows.Add([.. header]);
        }

        foreach (var row in rows.Take(MaxTableRows))
        {
            table.Rows.Add([.. row]);
        }

        return new PresentationElementSpec { Kind = PresentationElementSpecKind.Table, Table = table };
    }

    private static PresentationParagraphSpec Paragraph(RichTextBlock block, string bullet, PresentationTextStyle style)
    {
        var paragraph = new PresentationParagraphSpec { Bullet = bullet, Level = Math.Clamp(block.Level, 0, 4), Style = style };

        foreach (var span in block.Spans)
        {
            paragraph.Runs.Add(new PresentationRunSpec
            {
                Text = span.Text,
                Style = span.Bold || span.Italic || span.Strikethrough || span.Code
                    ? new PresentationTextStyle { Bold = span.Bold ? true : null, Italic = span.Italic ? true : null, Strikethrough = span.Strikethrough ? true : null, Font = span.Code ? "Consolas" : null }
                    : null,
                Link = Uri.TryCreate(span.Link, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeMailto)
                    ? new PresentationLinkSpec { Url = span.Link }
                    : null,
            });
        }

        if (paragraph.Runs.Count == 0 && !string.IsNullOrEmpty(block.Text))
        {
            paragraph.Runs.Add(new PresentationRunSpec { Text = block.Text });
        }

        return paragraph;
    }

    private static string Plain(RichTextBlock block)
    {
        return block.Spans.Count > 0 ? string.Concat(block.Spans.Select(span => span.Text)).Trim() : block.Text?.Trim() ?? string.Empty;
    }

    private static string Plain(RichTextCell cell)
    {
        return string.Concat(cell.Spans.Select(span => span.Text)).Trim();
    }
}
