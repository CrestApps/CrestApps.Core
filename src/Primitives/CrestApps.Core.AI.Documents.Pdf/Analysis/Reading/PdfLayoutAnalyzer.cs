using System.Runtime.InteropServices;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Works out what each block of text on a page is — heading, paragraph, list item, caption, running head or
/// foot, table text — and how the page is laid out: its columns, figures and tables, in reading order.
/// </summary>
/// <remarks>
/// The judgements are the ones a reader makes from the page alone: type noticeably larger than the body
/// text, and short, is a heading; text that repeats at the same edge of several pages is a running head;
/// "Figure 2" next to a picture is its caption. Type sizes are compared across every analysed page, so a
/// heading is a heading because of how the document sets its body text, not because of one page.
/// </remarks>
internal static class PdfLayoutAnalyzer
{
    /// <summary>
    /// The share of the page height at the top and bottom where running heads and feet sit.
    /// </summary>
    public const double EdgeZone = 0.08;

    private const int MaxSamplePages = 6;

    /// <summary>
    /// Analyses the layout of pages of a document.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <param name="pages">The one-based pages to analyse.</param>
    /// <param name="content">The tables and drawings the ingestion reader found, or <see langword="null"/> when they are not needed.</param>
    /// <param name="encodeImages">Whether placed pictures are encoded, for a result that shows them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The layout.</returns>
    public static PdfDocumentLayout Analyze(
        PdfDocument pdf,
        IReadOnlyList<int> pages,
        PdfIngestedContent content,
        bool encodeImages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pages);

        var layout = new PdfDocumentLayout();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(number);
            var pageLayout = new PdfPageLayout
            {
                PageNumber = number,
                Visible = PdfBox.VisibleArea(page),
                Rotation = ((page.Rotation.Value % 360) + 360) % 360,
            };

            pageLayout.Blocks.AddRange(ReadBlocks(page));
            AddImages(pageLayout, page, encodeImages);

            if (content is not null)
            {
                pageLayout.Tables.AddRange(content.TablesOn(number));
                pageLayout.Figures.AddRange(content.DrawingsOn(number));
            }

            layout.Pages.Add(pageLayout);
        }

        MarkRunningText(pdf, layout, cancellationToken);
        MarkRegionText(layout);

        layout.BodyFontSize = BodySize(layout);

        MarkHeadings(layout);
        MarkCaptionsAndLists(layout);

        foreach (var pageLayout in layout.Pages)
        {
            PutRunningTextAtEdges(pageLayout);
            AssignColumns(pageLayout);

            for (var index = 0; index < pageLayout.Blocks.Count; index++)
            {
                pageLayout.Blocks[index].Order = index + 1;
            }
        }

        return layout;
    }

    /// <summary>
    /// Reads a page's text as blocks in reading order, splitting a block where a heading runs into the text
    /// under it and where each list item starts.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The blocks.</returns>
    public static List<PdfLayoutBlock> ReadBlocks(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var blocks = new List<PdfLayoutBlock>();

        foreach (var block in PdfPageText.GetBlocks(page))
        {
            var lines = new List<PdfLayoutLine>(block.TextLines.Count);

            foreach (var line in block.TextLines)
            {
                var text = PdfTextPatterns.OneLine(line.Text);

                if (text.Length == 0)
                {
                    continue;
                }

                lines.Add(new PdfLayoutLine(text, PdfBox.From(line.BoundingBox), PdfTextStyle.Of(line.Words.SelectMany(word => word.Letters))));
            }

            Split(lines, blocks);
        }

        return blocks;
    }

    /// <summary>
    /// Returns whether a box sits in the band at the top or the bottom of a page where running text goes.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="visible">The page's visible area.</param>
    /// <param name="top">Whether the top band is tested; otherwise the bottom one.</param>
    /// <returns><see langword="true"/> when the box's centre is in the band.</returns>
    public static bool InEdgeZone(PdfBox box, PdfBox visible, bool top)
    {
        var center = (box.Top + box.Bottom) / 2;
        var band = visible.Height * EdgeZone;

        return top
            ? center >= visible.Top - band
            : center <= visible.Bottom + band;
    }

    private static void Split(List<PdfLayoutLine> lines, List<PdfLayoutBlock> blocks)
    {
        var current = new List<PdfLayoutLine>();

        foreach (var line in lines)
        {
            if (current.Count > 0 && StartsNewBlock(current, line))
            {
                blocks.Add(new PdfLayoutBlock(current));
                current = [];
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            blocks.Add(new PdfLayoutBlock(current));
        }
    }

    private static bool StartsNewBlock(List<PdfLayoutLine> current, PdfLayoutLine line)
    {
        var previous = current[^1];
        var smaller = Math.Min(previous.Style.Size, line.Style.Size);

        // A change of type size is a change of role: a heading set above its paragraph in one block.
        if (Math.Abs(previous.Style.Size - line.Style.Size) >= Math.Max(1.0, smaller * 0.12))
        {
            return true;
        }

        // A bold line opening a block of plain text is a heading run into what follows it.
        if (current.Count == 1 && previous.Style.Bold && !line.Style.Bold && previous.Text.Length <= 120)
        {
            return true;
        }

        // Every bullet starts an item of its own; a number does only inside a list, since a wrapped line of
        // prose can start with one.
        return PdfTextPatterns.IsBulleted(line.Text) ||
            (PdfTextPatterns.IsListItem(line.Text) && PdfTextPatterns.IsListItem(current[0].Text));
    }

    private static void AddImages(PdfPageLayout pageLayout, Page page, bool encode)
    {
        List<IPdfImage> images;

        try
        {
            images = [.. page.GetImages()];
        }
        catch (Exception)
        {
            return;
        }

        foreach (var image in images)
        {
            var box = PdfBox.From(image.BoundingBox);

            if (box.Width < 4 || box.Height < 4)
            {
                continue;
            }

            var region = new PdfRegion
            {
                Kind = PdfRegion.ImageKind,
                Page = pageLayout.PageNumber,
                Box = box,
                PixelWidth = image.WidthInSamples,
                PixelHeight = image.HeightInSamples,
                IsDecoration = box.Width < pageLayout.Visible.Width * 0.3 &&
                    (InEdgeZone(box, pageLayout.Visible, top: true) || InEdgeZone(box, pageLayout.Visible, top: false)),
            };

            if (encode)
            {
                var (bytes, mediaType) = PdfPageSvgRenderer.TryEncode(image);

                region.Content = bytes;
                region.MediaType = mediaType;
            }

            pageLayout.Figures.Add(region);
        }
    }

    private static void MarkRunningText(PdfDocument pdf, PdfDocumentLayout layout, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        var footers = new Dictionary<string, int>(StringComparer.Ordinal);
        var sampled = 0;

        foreach (var pageLayout in layout.Pages)
        {
            CountEdgeText(pageLayout.Blocks, pageLayout.Visible, headers, footers);
            sampled++;
        }

        // Repetition is only visible across pages, so a question about one or two pages of a longer document
        // also looks at a few others.
        if (layout.Pages.Count < 4 && pdf.NumberOfPages > layout.Pages.Count)
        {
            var analyzed = layout.Pages.Select(page => page.PageNumber).ToHashSet();

            foreach (var number in SamplePages(pdf.NumberOfPages, analyzed, MaxSamplePages - layout.Pages.Count))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = pdf.GetPage(number);

                CountEdgeText(ReadBlocks(page), PdfBox.VisibleArea(page), headers, footers);
                sampled++;
            }
        }

        var threshold = sampled >= 3
            ? Math.Max(2, (int)Math.Ceiling(sampled * 0.3))
            : 2;

        foreach (var pageLayout in layout.Pages)
        {
            foreach (var block in pageLayout.Blocks)
            {
                var top = InEdgeZone(block.Box, pageLayout.Visible, top: true);
                var bottom = !top && InEdgeZone(block.Box, pageLayout.Visible, top: false);

                if (!top && !bottom)
                {
                    continue;
                }

                var counts = top
                    ? headers
                    : footers;
                var repeated = counts.GetValueOrDefault(PdfTextPatterns.Signature(block.Text)) >= threshold;

                if (repeated || PdfTextPatterns.IsPageNumber(block.Text))
                {
                    block.Role = top
                        ? PdfLayoutRoles.Header
                        : PdfLayoutRoles.Footer;
                }
            }
        }
    }

    private static void CountEdgeText(List<PdfLayoutBlock> blocks, PdfBox visible, Dictionary<string, int> headers, Dictionary<string, int> footers)
    {
        // Each running text counts once per page, however many times the page repeats it.
        var seen = new HashSet<(bool Top, string Signature)>();

        foreach (var block in blocks)
        {
            var top = InEdgeZone(block.Box, visible, top: true);

            if (!top && !InEdgeZone(block.Box, visible, top: false))
            {
                continue;
            }

            var signature = PdfTextPatterns.Signature(block.Text);

            if (!seen.Add((top, signature)))
            {
                continue;
            }

            var counts = top
                ? headers
                : footers;

            CollectionsMarshal.GetValueRefOrAddDefault(counts, signature, out _)++;
        }
    }

    private static List<int> SamplePages(int pageCount, HashSet<int> exclude, int count)
    {
        var sample = new List<int>();

        if (count <= 0)
        {
            return sample;
        }

        var step = Math.Max(1.0, (double)pageCount / (count + 1));

        for (var position = 1.0; position <= pageCount && sample.Count < count; position += step)
        {
            var number = (int)Math.Round(position);

            if (number >= 1 && number <= pageCount && !exclude.Contains(number) && !sample.Contains(number))
            {
                sample.Add(number);
            }
        }

        return sample;
    }

    private static void MarkRegionText(PdfDocumentLayout layout)
    {
        foreach (var pageLayout in layout.Pages)
        {
            var pageArea = Math.Max(1, pageLayout.Visible.Width * pageLayout.Visible.Height);

            foreach (var block in pageLayout.Blocks)
            {
                if (block.Role is not null)
                {
                    continue;
                }

                if (pageLayout.Tables.Exists(table => table.Box is { } box && PdfBoxes.CenterInside(block.Box, box.Inflate(3))))
                {
                    block.Role = PdfLayoutRoles.Table;

                    continue;
                }

                // A picture behind most of the page is a scan or a background, and the text over it is the
                // page's text, not a label on a figure.
                if (block.Text.Length <= 80 &&
                    pageLayout.Figures.Exists(figure => figure.Box is { } box &&
                        box.Width * box.Height < pageArea * 0.5 &&
                        PdfBoxes.CenterInside(block.Box, box.Inflate(2))))
                {
                    block.Role = PdfLayoutRoles.FigureText;
                }
            }
        }
    }

    private static double BodySize(PdfDocumentLayout layout)
    {
        var weights = new Dictionary<double, int>();

        foreach (var block in layout.Pages.SelectMany(page => page.Blocks).Where(block => block.Role is null))
        {
            foreach (var line in block.Lines)
            {
                if (line.Style.Size <= 0)
                {
                    continue;
                }

                var size = Math.Round(line.Style.Size * 2, MidpointRounding.AwayFromZero) / 2;

                CollectionsMarshal.GetValueRefOrAddDefault(weights, size, out _) += line.Text.Length;
            }
        }

        var best = 0d;
        var bestWeight = 0;

        foreach (var (size, weight) in weights)
        {
            if (weight > bestWeight || (weight == bestWeight && size < best))
            {
                best = size;
                bestWeight = weight;
            }
        }

        return best;
    }

    private static void MarkHeadings(PdfDocumentLayout layout)
    {
        var body = layout.BodyFontSize;

        if (body <= 0)
        {
            return;
        }

        var threshold = body + Math.Max(1.0, body * 0.12);
        var blocks = layout.Pages.SelectMany(page => page.Blocks).Where(block => block.Role is null).ToList();
        var candidates = blocks.Where(block => block.Style.Size >= threshold && IsHeadingShape(block)).ToList();

        foreach (var size in candidates.Select(block => block.Style.Size).Distinct().OrderDescending())
        {
            if (layout.HeadingSizes.Count == 0 || layout.HeadingSizes[^1] - size > 0.6)
            {
                layout.HeadingSizes.Add(size);
            }
        }

        foreach (var block in candidates)
        {
            block.Role = PdfLayoutRoles.Heading;
            block.Level = Math.Min(6, TierOf(layout.HeadingSizes, block.Style.Size) + 1);
        }

        // A bold line on its own, in body-size type, is a heading too — unless the document sets most of its
        // text in bold, when bold says nothing.
        var bodyCharacters = 0;
        var boldCharacters = 0;

        foreach (var block in blocks.Where(block => block.Role is null))
        {
            bodyCharacters += block.Text.Length;

            if (block.Style.Bold)
            {
                boldCharacters += block.Text.Length;
            }
        }

        if (bodyCharacters == 0 || boldCharacters > bodyCharacters / 2)
        {
            return;
        }

        var boldLevel = Math.Min(6, layout.HeadingSizes.Count + 1);

        foreach (var block in blocks.Where(block => block.Role is null))
        {
            if (block.Lines.Count == 1 &&
                block.Style.Bold &&
                block.Text.Length <= 100 &&
                block.Style.Size >= body - 0.5 &&
                !block.Text.EndsWith('.') &&
                !PdfTextPatterns.IsBulleted(block.Text) &&
                !PdfTextPatterns.IsCaption(block.Text) &&
                IsHeadingShape(block))
            {
                block.Role = PdfLayoutRoles.Heading;
                block.Level = boldLevel;
            }
        }
    }

    private static int TierOf(List<double> tiers, double size)
    {
        for (var index = 0; index < tiers.Count; index++)
        {
            if (size >= tiers[index] - 0.6)
            {
                return index;
            }
        }

        return Math.Max(0, tiers.Count - 1);
    }

    private static bool IsHeadingShape(PdfLayoutBlock block)
    {
        if (block.Lines.Count > 3 || block.Text.Length > 200)
        {
            return false;
        }

        var letters = PdfTextPatterns.CountLetters(block.Text);
        var visible = block.Text.Count(character => !char.IsWhiteSpace(character));

        // A large number on its own — a figure in a dashboard tile — is not a heading.
        return letters >= 2 && letters >= visible * 0.4;
    }

    private static void MarkCaptionsAndLists(PdfDocumentLayout layout)
    {
        foreach (var pageLayout in layout.Pages)
        {
            foreach (var block in pageLayout.Blocks)
            {
                if (block.Role is not null)
                {
                    continue;
                }

                if (PdfTextPatterns.IsCaption(block.Text))
                {
                    var region = FindCaptioned(pageLayout, block);

                    if (region is not null)
                    {
                        block.Role = PdfLayoutRoles.Caption;
                        block.CaptionOf = region;
                        region.Caption ??= PdfTextPatterns.OneLine(block.Text);

                        continue;
                    }
                }

                block.Role = PdfTextPatterns.IsListItem(block.Lines[0].Text)
                    ? PdfLayoutRoles.ListItem
                    : PdfLayoutRoles.Paragraph;
            }
        }
    }

    private static PdfRegion FindCaptioned(PdfPageLayout pageLayout, PdfLayoutBlock block)
    {
        PdfRegion best = null;
        var bestGap = double.MaxValue;

        foreach (var region in pageLayout.Figures.Concat(pageLayout.Tables))
        {
            if (region.IsDecoration || region.Box is not { } box)
            {
                continue;
            }

            var gap = PdfBoxes.VerticalGap(block.Box, box);
            var centerDistance = Math.Abs(((block.Box.Left + block.Box.Right) / 2) - ((box.Left + box.Right) / 2));

            if (gap <= 40 && (PdfBoxes.HorizontalOverlap(block.Box, box) > 0 || centerDistance < 60) && gap < bestGap)
            {
                best = region;
                bestGap = gap;
            }
        }

        return best;
    }

    private static void PutRunningTextAtEdges(PdfPageLayout pageLayout)
    {
        var headers = pageLayout.Blocks.Where(block => block.Role == PdfLayoutRoles.Header).ToList();
        var footers = pageLayout.Blocks.Where(block => block.Role == PdfLayoutRoles.Footer).ToList();

        if (headers.Count == 0 && footers.Count == 0)
        {
            return;
        }

        // A running head is read before the page and a running foot after it, wherever the file draws them.
        var body = pageLayout.Blocks.Where(block => block.Role is not (PdfLayoutRoles.Header or PdfLayoutRoles.Footer)).ToList();

        pageLayout.Blocks.Clear();
        pageLayout.Blocks.AddRange(headers);
        pageLayout.Blocks.AddRange(body);
        pageLayout.Blocks.AddRange(footers);
    }

    private static void AssignColumns(PdfPageLayout pageLayout)
    {
        var body = pageLayout.Blocks
            .Where(block => block.Role is PdfLayoutRoles.Paragraph or PdfLayoutRoles.ListItem or PdfLayoutRoles.Heading or PdfLayoutRoles.Caption)
            .ToList();

        if (body.Count == 0)
        {
            return;
        }

        var left = body.Min(block => block.Box.Left);
        var right = body.Max(block => block.Box.Right);
        var width = Math.Max(1, right - left);
        var narrow = body.Where(block => block.Box.Width <= width * 0.6).OrderBy(block => block.Box.Left).ToList();
        var narrowCharacters = narrow.Sum(block => block.Text.Length);
        var totalCharacters = body.Sum(block => block.Text.Length);
        var bands = new List<(double Left, double Right, int Characters)>();

        if (narrow.Count >= 2 && narrowCharacters >= totalCharacters * 0.3)
        {
            foreach (var block in narrow)
            {
                if (bands.Count > 0 && block.Box.Left <= bands[^1].Right - 2)
                {
                    var last = bands[^1];

                    bands[^1] = (last.Left, Math.Max(last.Right, block.Box.Right), last.Characters + block.Text.Length);

                    continue;
                }

                bands.Add((block.Box.Left, block.Box.Right, block.Text.Length));
            }

            bands.RemoveAll(band => band.Characters < narrowCharacters * 0.1);
        }

        if (bands.Count <= 1)
        {
            pageLayout.Columns.Add((left, right));

            foreach (var block in pageLayout.Blocks)
            {
                block.Column = 1;
            }

            return;
        }

        foreach (var band in bands)
        {
            pageLayout.Columns.Add((band.Left, band.Right));
        }

        foreach (var block in pageLayout.Blocks)
        {
            var center = (block.Box.Left + block.Box.Right) / 2;

            block.Column = null;

            if (block.Box.Width > width * 0.6)
            {
                continue;
            }

            for (var index = 0; index < pageLayout.Columns.Count; index++)
            {
                if (center >= pageLayout.Columns[index].Left - 2 && center <= pageLayout.Columns[index].Right + 2)
                {
                    block.Column = index + 1;

                    break;
                }
            }
        }
    }
}
