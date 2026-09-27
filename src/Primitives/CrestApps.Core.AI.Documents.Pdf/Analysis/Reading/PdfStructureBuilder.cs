using CrestApps.Core.AI.Ingestion.Pdf.Services;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Turns a document's layout into its logical structure: headings, paragraphs, list items, tables and
/// figures in reading order, with running heads, page numbers and the text inside tables and charts left
/// out, since a reader does not read those as part of the flow.
/// </summary>
internal static class PdfStructureBuilder
{
    /// <summary>
    /// Builds the structure of the analysed pages.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The elements, in reading order.</returns>
    public static List<PdfStructureElement> Build(PdfDocumentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var elements = new List<PdfStructureElement>();

        foreach (var page in layout.Pages)
        {
            var emitted = new HashSet<PdfRegion>();
            var figures = page.Figures.Where(IsContentFigure).ToList();

            foreach (var block in page.Blocks)
            {
                switch (block.Role)
                {
                    case PdfLayoutRoles.Header:
                    case PdfLayoutRoles.Footer:
                    case PdfLayoutRoles.FigureText:
                        continue;

                    case PdfLayoutRoles.Table:
                        {
                            // A table takes the place of its first cell in the reading order.
                            var table = page.Tables.Find(region => region.Box is { } box && PdfBoxes.CenterInside(block.Box, box.Inflate(3)));

                            if (table is not null && emitted.Add(table))
                            {
                                elements.Add(RegionElement(page, table));
                            }

                            continue;
                        }

                    case PdfLayoutRoles.Caption when block.CaptionOf is not null:
                        if (emitted.Add(block.CaptionOf))
                        {
                            elements.Add(RegionElement(page, block.CaptionOf));
                        }

                        continue;
                }

                // A figure is read where it stands: before the first text that starts below it.
                foreach (var figure in figures)
                {
                    if (figure.Box is { } box &&
                        box.Top > block.Box.Top + 2 &&
                        PdfBoxes.HorizontalOverlap(box, block.Box) > 0 &&
                        emitted.Add(figure))
                    {
                        elements.Add(RegionElement(page, figure));
                    }
                }

                elements.Add(TextElement(page, block));
            }

            foreach (var region in page.Tables.Concat(figures))
            {
                if (emitted.Add(region))
                {
                    elements.Add(RegionElement(page, region));
                }
            }
        }

        return elements;
    }

    /// <summary>
    /// Reflows a block's lines into running text, rejoining words hyphenated across a line break.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The text on one line.</returns>
    public static string Reflow(PdfLayoutBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return PdfTextPatterns.OneLine(PdfTextNormalizer.Normalize(block.Text));
    }

    private static bool IsContentFigure(PdfRegion figure)
    {
        // A picture smaller than a word — a bullet, a rule, an icon — is ornament, not a figure.
        return !figure.IsDecoration && (figure.Box is not { } box || (box.Width >= 16 && box.Height >= 16));
    }

    private static PdfStructureElement TextElement(PdfPageLayout page, PdfLayoutBlock block)
    {
        var box = PdfBoxes.ToArray(block.Box, page.Visible);

        switch (block.Role)
        {
            case PdfLayoutRoles.Heading:
                return new PdfStructureElement
                {
                    Type = PdfStructureElement.HeadingType,
                    Page = page.PageNumber,
                    Level = block.Level ?? 1,
                    Text = PdfTextPatterns.OneLine(block.Text),
                    Box = box,
                };

            case PdfLayoutRoles.ListItem:
                {
                    var text = PdfTextPatterns.StripListMarker(Reflow(block), out var marker);

                    return new PdfStructureElement
                    {
                        Type = PdfStructureElement.ListItemType,
                        Page = page.PageNumber,
                        Text = text,
                        Marker = marker,
                        Box = box,
                    };
                }

            default:
                return new PdfStructureElement
                {
                    Type = block.Role == PdfLayoutRoles.Caption
                        ? PdfStructureElement.CaptionType
                        : PdfStructureElement.ParagraphType,
                    Page = page.PageNumber,
                    Text = Reflow(block),
                    Box = box,
                };
        }
    }

    private static PdfStructureElement RegionElement(PdfPageLayout page, PdfRegion region)
    {
        return new PdfStructureElement
        {
            Type = region.Kind == PdfRegion.TableKind
                ? PdfStructureElement.TableType
                : PdfStructureElement.FigureType,
            Page = page.PageNumber,
            Text = region.Caption,
            Region = region,
            Box = region.Box is { } box
                ? PdfBoxes.ToArray(box, page.Visible)
                : null,
        };
    }
}
