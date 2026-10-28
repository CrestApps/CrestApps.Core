using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// One layout of a document into pages: placing every block, and, when it draws, writing the pages.
/// </summary>
internal sealed class PdfLayoutPass
{
    private readonly PdfLayoutDocument _document;
    private readonly bool _draw;
    private readonly IReadOnlyDictionary<string, PdfLayoutTarget> _references;
    private readonly int _knownPageCount;
    private readonly Dictionary<string, PdfLayoutTarget> _targets = new(StringComparer.Ordinal);
    private readonly List<(int Level, string Title, PdfLayoutTarget Target)> _outline = [];
    private readonly List<PageState> _pages = [];
    private readonly Dictionary<PdfLayoutBlock, object> _laid = [];
    private readonly List<int> _sectionPageCounts = [];

    private PdfLayoutSection _section;
    private int _sectionIndex;
    private int _sectionFirstPage;
    private int _nextNumber = 1;
    private PageState _page;
    private double _y;

    // The space the block placed last keeps below it. Between two blocks the larger of its space below and
    // the next one's space above is left, not both.
    private double _pendingSpace;

    // Whether nothing has been placed on the page yet: the first paragraph of a page leaves no space above it.
    private bool _firstOnPage;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutPass"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="draw">Whether to write the pages, or only to find where everything goes.</param>
    /// <param name="references">Where each bookmark landed in an earlier pass, for page reference fields; or <see langword="null"/>.</param>
    /// <param name="knownPageCount">The number of pages an earlier pass found, or 0.</param>
    public PdfLayoutPass(PdfLayoutDocument document, bool draw, IReadOnlyDictionary<string, PdfLayoutTarget> references, int knownPageCount)
    {
        _document = document;
        _draw = draw;
        _references = references;
        _knownPageCount = knownPageCount;

        if (draw)
        {
            Output = new PdfDocument();
        }
    }

    /// <summary>
    /// Gets the written document, when the pass draws.
    /// </summary>
    public PdfDocument Output { get; }

    /// <summary>
    /// Gets where each bookmark landed.
    /// </summary>
    public IReadOnlyDictionary<string, PdfLayoutTarget> Targets => _targets;

    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public int PageCount => _pages.Count;

    private double BodyTop => _section.PageSetup.TopMargin;

    private double BodyBottom => _section.PageSetup.PageHeight - _section.PageSetup.BottomMargin;

    private double BodyHeight => Math.Max(1, BodyBottom - BodyTop);

    private double AreaLeft => _section.PageSetup.LeftMargin;

    private double AreaWidth => Math.Max(1, _section.PageSetup.UsableWidth);

    private bool PageIsEmpty => _y <= BodyTop + 0.01;

    /// <summary>
    /// Lays the document out.
    /// </summary>
    public void Run()
    {
        for (_sectionIndex = 0; _sectionIndex < _document.Sections.Count; _sectionIndex++)
        {
            _section = _document.Sections[_sectionIndex];
            _laid.Clear();

            if (_section.PageSetup.StartingNumber is int start)
            {
                _nextNumber = start;
            }

            _sectionFirstPage = _pages.Count;
            NewPage();

            for (var index = 0; index < _section.Blocks.Count; index++)
            {
                Place(index);
            }

            _sectionPageCounts.Add(_pages.Count - _sectionFirstPage);
        }

        if (!_draw)
        {
            return;
        }

        foreach (var page in _pages)
        {
            DrawRunningHeads(page);
            page.Canvas.Commit();
            WriteLinks(page);
        }

        WriteOutline();
        Output.Info.Title = _document.Title;
        Output.Info.Author = _document.Author;
        Output.Info.Subject = _document.Subject;
        Output.Info.Keywords = _document.Keywords;
        Output.Info.Creator = "CrestApps";
        Output.Info.Producer = "CrestApps";
    }

    private void Place(int index)
    {
        var block = _section.Blocks[index];

        switch (block)
        {
            case PdfLayoutPageBreak:
                NewPage();

                break;

            case PdfLayoutFrame frame:
                // A frame takes no room, but the paragraph after it is no longer the first on its page.
                DrawFrame(frame);
                _firstOnPage = false;
                _pendingSpace = 0;

                break;

            case PdfLayoutParagraph paragraph:
                KeepWithNext(index);
                PlaceParagraph(Laid(paragraph));

                break;

            case PdfLayoutTable table:
                KeepWithNext(index);
                PlaceTable(table, LaidTable(table));

                break;

            case PdfLayoutGraphic graphic:
                KeepWithNext(index);
                PlaceGraphic(graphic);

                break;
        }
    }

    private void NewPage()
    {
        var setup = _section.PageSetup;

        _page = new PageState
        {
            Section = _section,
            SectionIndex = _sectionIndex,
            IsFirstOfSection = _pages.Count == _sectionFirstPage,
            Index = _pages.Count,
            Number = _nextNumber++,
            Width = setup.PageWidth,
            Height = setup.PageHeight,
        };

        if (_draw)
        {
            var page = Output.AddPage();

            page.MediaBox = new PdfRectangle(0, 0, setup.PageWidth, setup.PageHeight);
            _page.Page = page;
            _page.Canvas = PdfCanvas.ForPage(page);
        }

        _pages.Add(_page);
        _y = BodyTop;
        _pendingSpace = 0;
        _firstOnPage = true;
    }

    /// <summary>
    /// Starts a new page when a block that keeps with the next, and the start of the next, do not fit on this one.
    /// </summary>
    private void KeepWithNext(int index)
    {
        if (!KeepsWithNext(_section.Blocks[index]) || PageIsEmpty)
        {
            return;
        }

        var needed = Needed(index, 0);

        if (_y + needed > BodyBottom && needed <= BodyHeight)
        {
            NewPage();
        }
    }

    private double Needed(int index, int depth)
    {
        var block = _section.Blocks[index];
        var height = Height(block);

        if (!KeepsWithNext(block) || index + 1 >= _section.Blocks.Count || depth > 8)
        {
            return height;
        }

        var next = _section.Blocks[index + 1];

        return KeepsWithNext(next)
            ? height + Needed(index + 1, depth + 1)
            : height + FirstPart(next);
    }

    private bool KeepsWithNext(PdfLayoutBlock block)
    {
        return block switch
        {
            PdfLayoutParagraph paragraph => Laid(paragraph).Format.KeepWithNext == true,
            PdfLayoutGraphic graphic => graphic.KeepWithNext,
            _ => false,
        };
    }

    private double Height(PdfLayoutBlock block)
    {
        return block switch
        {
            PdfLayoutParagraph paragraph => Laid(paragraph).Height,
            PdfLayoutTable table => LaidTable(table).Sum(row => row.Height),
            PdfLayoutGraphic graphic => graphic.SpaceBefore + graphic.Height + graphic.SpaceAfter,
            _ => 0,
        };
    }

    private double FirstPart(PdfLayoutBlock block)
    {
        switch (block)
        {
            case PdfLayoutParagraph paragraph:
                {
                    var laid = Laid(paragraph);

                    return laid.Lines.Count <= 1
                        ? laid.Height
                        : laid.SpaceBefore + laid.TopInset + laid.Lines[0].Height;
                }

            case PdfLayoutTable table:
                {
                    var rows = LaidTable(table);
                    var headings = rows.TakeWhile(row => row.Row.HeadingFormat).Sum(row => row.Height);

                    return headings + rows.SkipWhile(row => row.Row.HeadingFormat).Take(1).Sum(row => row.Height);
                }

            default:
                return Height(block);
        }
    }

    private PdfLaidParagraph Laid(PdfLayoutParagraph paragraph)
    {
        if (_laid.TryGetValue(paragraph, out var cached))
        {
            return (PdfLaidParagraph)cached;
        }

        var laid = PdfParagraphLayouter.Layout(paragraph, Resolve(paragraph, null), AreaWidth, BodyFieldText);

        _laid[paragraph] = laid;

        return laid;
    }

    private List<LaidRow> LaidTable(PdfLayoutTable table)
    {
        if (_laid.TryGetValue(table, out var cached))
        {
            return (List<LaidRow>)cached;
        }

        var rows = new List<LaidRow>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var cells = new List<List<PdfLaidParagraph>>(row.Cells.Count);
            var content = 0d;
            var bottomBorder = 0d;
            var topBorder = 0d;

            for (var column = 0; column < row.Cells.Count && column < table.Columns.Count; column++)
            {
                var borders = row.Cells[column].Borders.Over(row.Borders).Over(table.Borders);

                // A cell's right border takes its width from the cell.
                var width = Math.Max(1, table.Columns[column].Width - Width(borders.Right) - table.LeftPadding - table.RightPadding);
                var paragraphs = new List<PdfLaidParagraph>();

                foreach (var paragraph in row.Cells[column].Paragraphs)
                {
                    var format = paragraph.Format.Over(row.Format).Over(table.Columns[column].Format).Over(table.Format);

                    paragraphs.Add(PdfParagraphLayouter.Layout(paragraph, Resolve(paragraph, format), width, BodyFieldText));
                }

                bottomBorder = Math.Max(bottomBorder, Width(borders.Bottom));
                topBorder = Math.Max(topBorder, Width(borders.Top));
                cells.Add(paragraphs);
                content = Math.Max(content, StackHeight(paragraphs));
            }

            // A row is as tall as its tallest cell and its padding, with room below for its bottom border; a
            // row starting the table on a page also keeps room above for its top border.
            rows.Add(new LaidRow(row, cells, content + table.TopPadding + table.BottomPadding + bottomBorder, bottomBorder, topBorder));
        }

        _laid[table] = rows;

        return rows;
    }

    private PdfLayoutParagraphFormat Resolve(PdfLayoutParagraph paragraph, PdfLayoutParagraphFormat container, string defaultStyle = null)
    {
        var format = container ?? paragraph.Format;

        return format.Over(_document.Styles.Resolve(paragraph.Style ?? defaultStyle));
    }

    private string BodyFieldText(PdfLayoutField field)
    {
        return field.Kind switch
        {
            PdfLayoutFieldKind.PageReference => _references is not null && field.Bookmark is not null && _references.TryGetValue(field.Bookmark, out var target)
                ? PdfLayoutNumbers.Format(target.PageNumber, field.Format)
                : "0",
            PdfLayoutFieldKind.NumPages => PdfLayoutNumbers.Format(Math.Max(1, _knownPageCount), field.Format),
            _ => PdfLayoutNumbers.Format(_page?.Number ?? 1, field.Format),
        };
    }

    private void PlaceParagraph(PdfLaidParagraph laid)
    {
        var before = _firstOnPage
            ? 0
            : laid.SpaceBefore;

        _y += Math.Max(_pendingSpace, before);

        var lineIndex = 0;
        var first = true;

        while (lineIndex < laid.Lines.Count || (first && laid.Lines.Count == 0))
        {
            var top = first
                ? laid.TopInset
                : 0;

            var count = 0;
            var height = 0d;

            while (lineIndex + count < laid.Lines.Count)
            {
                var line = laid.Lines[lineIndex + count];
                var isLast = lineIndex + count == laid.Lines.Count - 1;
                var bottom = isLast
                    ? laid.BottomInset
                    : 0;

                if (_y + top + height + line.Height + bottom > BodyBottom + 0.01 && (count > 0 || !PageIsEmpty))
                {
                    break;
                }

                height += line.Height;
                count++;
            }

            if (count == 0 && laid.Lines.Count > 0)
            {
                NewPage();

                continue;
            }

            var last = lineIndex + count >= laid.Lines.Count;
            var fragmentHeight = top + height;

            if (last)
            {
                fragmentHeight += laid.BottomInset;
            }

            DrawParagraph(laid, lineIndex, count, AreaLeft, AreaWidth, _y, first, last);

            if (first && laid.Format.OutlineLevel is >= 1 and <= 9 && !string.IsNullOrWhiteSpace(laid.Text))
            {
                _outline.Add((laid.Format.OutlineLevel.Value, laid.Text, new PdfLayoutTarget(_page.Index, _page.Number, _y)));
            }

            _y += fragmentHeight;
            lineIndex += count;
            first = false;

            if (lineIndex < laid.Lines.Count)
            {
                NewPage();
            }
        }

        _pendingSpace = laid.SpaceAfter;
        _firstOnPage = false;
    }

    /// <summary>
    /// Draws lines of a laid-out paragraph, with the part of its shading and borders they carry, and
    /// records where its bookmarks are.
    /// </summary>
    private void DrawParagraph(PdfLaidParagraph laid, int from, int count, double areaLeft, double areaWidth, double top, bool first, bool last)
    {
        var format = laid.Format;
        var borders = format.Borders;
        var linesHeight = laid.Lines.Skip(from).Take(count).Sum(line => line.Height);
        var textTop = top;

        // The top border's room is above the first page's lines; the bottom border's below the last page's.
        if (first)
        {
            textTop += laid.TopInset;
        }

        var bottom = textTop + linesHeight;

        if (last)
        {
            bottom += laid.BottomInset;
        }

        var boxLeft = areaLeft + (format.LeftIndent ?? 0) - (borders.DistanceFromLeft ?? 0);
        var boxRight = areaLeft + areaWidth - (format.RightIndent ?? 0);
        var canvas = _page.Canvas;

        if (canvas is not null && format.Shading is { } shading)
        {
            canvas.DrawRectangle(boxLeft, top, boxRight - boxLeft, bottom - top, new PdfBrush(shading));
        }

        if (canvas is not null && first && borders.Top.IsDrawn())
        {
            var width = borders.Top.Width ?? 0.5;

            canvas.DrawLine(boxLeft, top + (width / 2), boxRight, top + (width / 2), Pen(borders.Top));
        }

        if (canvas is not null && last && borders.Bottom.IsDrawn())
        {
            var width = borders.Bottom.Width ?? 0.5;

            canvas.DrawLine(boxLeft, bottom - (width / 2), boxRight, bottom - (width / 2), Pen(borders.Bottom));
        }

        if (canvas is not null && borders.Left.IsDrawn())
        {
            var width = borders.Left.Width ?? 0.5;

            canvas.DrawLine(boxLeft - (width / 2), textTop, boxLeft - (width / 2), textTop + linesHeight, Pen(borders.Left));
        }

        var y = textTop;

        foreach (var line in laid.Lines.Skip(from).Take(count))
        {
            foreach (var bookmark in line.Bookmarks)
            {
                _targets.TryAdd(bookmark, new PdfLayoutTarget(_page.Index, _page.Number, y));
            }

            DrawLine(line, areaLeft, y);
            y += line.Height;
        }
    }

    private void DrawLine(PdfLaidLine line, double areaLeft, double top)
    {
        var canvas = _page.Canvas;
        var baseline = top + line.Baseline;
        PdfLaidItem linkStart = null;
        var linkLeft = 0d;
        var linkRight = 0d;

        foreach (var item in line.Items)
        {
            var x = areaLeft + line.Left + item.X;

            if (canvas is not null)
            {
                if (item.Image is not null)
                {
                    canvas.DrawImage(item.Image, x, baseline - item.ImageHeight, item.Width, item.ImageHeight);
                }
                else if (!item.IsSpace)
                {
                    canvas.DrawText(item.Text, item.Font, item.Size, x, baseline, new PdfBrush(item.Color));
                }

                if (item.Underline && item.Image is null)
                {
                    var thickness = Math.Max(0.4, item.Size * 0.05);

                    canvas.DrawLine(x, baseline + (item.Size * 0.12), x + item.Width, baseline + (item.Size * 0.12), new PdfPen(item.Color, thickness));
                }
            }

            // Pieces next to each other that go to the same place are one link.
            if (linkStart is not null && !Equals(linkStart.Link, item.Link))
            {
                _page.Links.Add((new PdfLayoutBox(linkLeft, top, linkRight - linkLeft, line.Height), linkStart.Link));
                linkStart = null;
            }

            if (item.Link is not null)
            {
                if (linkStart is null)
                {
                    linkStart = item;
                    linkLeft = x;
                }

                linkRight = x + item.Width;
            }
        }

        if (linkStart is not null)
        {
            _page.Links.Add((new PdfLayoutBox(linkLeft, top, linkRight - linkLeft, line.Height), linkStart.Link));
        }
    }

    private void PlaceTable(PdfLayoutTable table, List<LaidRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        // A table keeps no space of its own above or below it.
        _y += _pendingSpace;
        _pendingSpace = 0;

        var total = rows.Sum(row => row.Height);

        if (table.KeepTogether && !PageIsEmpty && _y + total > BodyBottom && total <= BodyHeight)
        {
            NewPage();
        }

        var headings = rows.TakeWhile(row => row.Row.HeadingFormat).ToList();
        var startsPage = true;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var topRoom = startsPage
                ? row.TopBorder
                : 0;

            if (_y + topRoom + row.Height > BodyBottom + 0.01 && !PageIsEmpty)
            {
                NewPage();
                startsPage = true;

                // Heading rows start every page the table runs onto, unless the row is one of them.
                if (index >= headings.Count)
                {
                    foreach (var heading in headings)
                    {
                        DrawRow(table, heading, startsPage);
                        startsPage = false;
                    }
                }
            }

            DrawRow(table, row, startsPage);
            startsPage = false;
        }

        _firstOnPage = false;
    }

    private void DrawRow(PdfLayoutTable table, LaidRow laid, bool startsPage)
    {
        var canvas = _page.Canvas;

        // Every column is moved right by the first cell's left border. A table without an indent of its own
        // is moved left by that border and its padding, so the text in its first column lines up with the
        // paragraphs around it.
        var leftBorder = table.Rows.Count > 0 && table.Rows[0].Cells.Count > 0
            ? Width(table.Rows[0].Cells[0].Borders.Over(table.Rows[0].Borders).Over(table.Borders).Left)
            : 0;

        var x = AreaLeft + leftBorder + (table.LeftIndent ?? -(leftBorder + table.LeftPadding));
        var top = _y;

        if (startsPage)
        {
            top += laid.TopBorder;
        }

        var inner = laid.Height - laid.BottomBorder;

        for (var column = 0; column < laid.Cells.Count; column++)
        {
            var width = table.Columns[column].Width;
            var cell = laid.Row.Cells[column];
            var paragraphs = laid.Cells[column];
            var borders = cell.Borders.Over(laid.Row.Borders).Over(table.Borders);
            var topWidth = Width(borders.Top);
            var bottomWidth = Width(borders.Bottom);
            var leftWidth = Width(borders.Left);
            var rightWidth = Width(borders.Right);

            // The cell's area is its column less its right border; the other borders sit outside it, in the
            // room the column before and the rows keep for them.
            var innerWidth = width - rightWidth;

            if (canvas is not null && (cell.Shading ?? laid.Row.Shading) is { } shading)
            {
                canvas.DrawRectangle(x, top, innerWidth, inner, new PdfBrush(shading));
            }

            var free = inner - table.TopPadding - table.BottomPadding - StackHeight(paragraphs);
            var offset = laid.Row.VerticalAlignment switch
            {
                PdfLayoutVerticalAlignment.Center => free / 2,
                PdfLayoutVerticalAlignment.Bottom => free,
                _ => 0,
            };

            DrawStack(paragraphs, x + table.LeftPadding, innerWidth - table.LeftPadding - table.RightPadding, top + table.TopPadding + offset);

            if (canvas is not null)
            {
                DrawBorder(canvas, borders.Top, x - leftWidth, top - (topWidth / 2), x + innerWidth + rightWidth, top - (topWidth / 2));
                DrawBorder(canvas, borders.Bottom, x - leftWidth, top + inner + (bottomWidth / 2), x + innerWidth + rightWidth, top + inner + (bottomWidth / 2));
                DrawBorder(canvas, borders.Left, x - (leftWidth / 2), top, x - (leftWidth / 2), top + inner + bottomWidth);
                DrawBorder(canvas, borders.Right, x + innerWidth + (rightWidth / 2), top, x + innerWidth + (rightWidth / 2), top + inner + bottomWidth);
            }

            x += width;
        }

        _y = top + laid.Height;
    }

    private static double Width(PdfLayoutBorder border)
    {
        return border.IsDrawn()
            ? border.Width ?? 0.5
            : 0;
    }

    private static void DrawBorder(PdfCanvas canvas, PdfLayoutBorder border, double x1, double y1, double x2, double y2)
    {
        if (border.IsDrawn())
        {
            canvas.DrawLine(x1, y1, x2, y2, Pen(border));
        }
    }

    private static PdfPen Pen(PdfLayoutBorder border)
    {
        return new PdfPen(border.Color ?? new PdfColor(0, 0, 0), border.Width ?? 0.5);
    }

    private void PlaceGraphic(PdfLayoutGraphic graphic)
    {
        var before = Math.Max(_pendingSpace, graphic.SpaceBefore);

        if (_y + before + graphic.Height > BodyBottom + 0.01 && !PageIsEmpty)
        {
            NewPage();
            before = graphic.SpaceBefore;
        }

        _y += before;

        var x = graphic.Alignment switch
        {
            PdfLayoutAlignment.Right => AreaLeft + AreaWidth - graphic.Width,
            PdfLayoutAlignment.Center => AreaLeft + ((AreaWidth - graphic.Width) / 2),
            _ => AreaLeft,
        };

        if (_page.Canvas is not null)
        {
            _page.Canvas.Save();
            graphic.Draw(_page.Canvas, x, _y);
            _page.Canvas.Restore();
        }

        _y = Math.Min(_y + graphic.Height, BodyBottom);
        _pendingSpace = graphic.SpaceAfter;
        _firstOnPage = false;
    }

    private void DrawFrame(PdfLayoutFrame frame)
    {
        var setup = _section.PageSetup;
        var left = frame.LeftFromPage
            ? frame.Left
            : setup.LeftMargin + frame.Left;

        var top = frame.TopFromPage
            ? frame.Top
            : setup.TopMargin + frame.Top;

        if (_page.Canvas is not null && frame.Fill is { } fill)
        {
            _page.Canvas.DrawRectangle(left, top, frame.Width, frame.Height, new PdfBrush(fill));
        }

        var paragraphs = frame.Paragraphs.Select(paragraph => PdfParagraphLayouter.Layout(paragraph, Resolve(paragraph, null), frame.Width, BodyFieldText)).ToList();

        DrawStack(paragraphs, left, frame.Width, top);
    }

    /// <summary>
    /// Returns the height of paragraphs set one under another, the space between two the larger of the
    /// first's space below and the second's space above.
    /// </summary>
    private static double StackHeight(List<PdfLaidParagraph> paragraphs)
    {
        var height = 0d;
        var pending = 0d;

        foreach (var paragraph in paragraphs)
        {
            height += Math.Max(pending, paragraph.SpaceBefore) + paragraph.TopInset + paragraph.LinesHeight + paragraph.BottomInset;
            pending = paragraph.SpaceAfter;
        }

        return height + pending;
    }

    /// <summary>
    /// Draws paragraphs one under another, spaced as <see cref="StackHeight"/> measures them.
    /// </summary>
    private void DrawStack(List<PdfLaidParagraph> paragraphs, double left, double width, double top)
    {
        var y = top;
        var pending = 0d;

        foreach (var paragraph in paragraphs)
        {
            y += Math.Max(pending, paragraph.SpaceBefore);
            DrawParagraph(paragraph, 0, paragraph.Lines.Count, left, width, y, true, true);
            y += paragraph.TopInset + paragraph.LinesHeight + paragraph.BottomInset;
            pending = paragraph.SpaceAfter;
        }
    }

    /// <summary>
    /// Draws a page's header and footer, with its page number and the page counts filled in.
    /// </summary>
    private void DrawRunningHeads(PageState page)
    {
        var section = page.Section;
        var setup = section.PageSetup;
        var useFirst = setup.DifferentFirstPageHeaderFooter && page.IsFirstOfSection;
        var header = useFirst
            ? section.Headers.FirstPage
            : section.Headers.Primary;

        var footer = useFirst
            ? section.Footers.FirstPage
            : section.Footers.Primary;

        var previousPage = _page;

        _page = page;
        _section = section;

        string FieldText(PdfLayoutField field)
        {
            return field.Kind switch
            {
                PdfLayoutFieldKind.NumPages => PdfLayoutNumbers.Format(_pages.Count, field.Format),
                PdfLayoutFieldKind.SectionPages => PdfLayoutNumbers.Format(_sectionPageCounts[page.SectionIndex], field.Format),
                PdfLayoutFieldKind.PageReference => BodyFieldText(field),
                _ => PdfLayoutNumbers.Format(page.Number, field.Format),
            };
        }

        var headerParagraphs = header.Paragraphs.Select(paragraph => PdfParagraphLayouter.Layout(paragraph, Resolve(paragraph, null, PdfLayoutStyles.Header), AreaWidth, FieldText)).ToList();
        var footerParagraphs = footer.Paragraphs.Select(paragraph => PdfParagraphLayouter.Layout(paragraph, Resolve(paragraph, null, PdfLayoutStyles.Footer), AreaWidth, FieldText)).ToList();

        DrawStack(headerParagraphs, AreaLeft, AreaWidth, setup.HeaderDistance);

        // The footer, its space below included, ends its distance above the page's bottom edge.
        DrawStack(footerParagraphs, AreaLeft, AreaWidth, setup.PageHeight - setup.FooterDistance - StackHeight(footerParagraphs));

        _page = previousPage;
    }

    private void WriteLinks(PageState page)
    {
        if (page.Links.Count == 0)
        {
            return;
        }

        var annotations = page.Page.Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(Output);
            page.Page.Elements["/Annots"] = annotations;
        }

        foreach (var (box, link) in page.Links)
        {
            var annotation = new PdfDictionary(Output);

            annotation.Elements.SetName("/Type", "/Annot");
            annotation.Elements.SetName("/Subtype", "/Link");
            annotation.Elements["/Rect"] = new PdfRectangle(box.Left, page.Height - box.Top - box.Height, box.Left + box.Width, page.Height - box.Top);
            annotation.Elements["/Border"] = new PdfArray(Output, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));

            if (link.IsWeb)
            {
                var action = new PdfDictionary(Output);

                action.Elements.SetName("/S", "/URI");
                action.Elements.SetString("/URI", link.Target);
                annotation.Elements["/A"] = action;
            }
            else if (_targets.TryGetValue(link.Target ?? string.Empty, out var target))
            {
                annotation.Elements["/Dest"] = Destination(target);
            }
            else
            {
                continue;
            }

            annotations.Elements.Add(Output.AddObject(annotation));
        }
    }

    private PdfArray Destination(PdfLayoutTarget target)
    {
        var page = _pages[target.PageIndex];

        return new PdfArray(Output, Output.AddObject(page.Page), new PdfName("/XYZ"), new PdfInteger(0), new PdfReal(page.Height - target.Top), PdfNull.Value);
    }

    /// <summary>
    /// Writes the document outline from the headings, nested by level.
    /// </summary>
    private void WriteOutline()
    {
        if (_outline.Count == 0)
        {
            return;
        }

        var root = new OutlineNode(0, null, default);
        var parents = new List<OutlineNode> { root };

        foreach (var (level, title, target) in _outline)
        {
            while (parents.Count > 1 && parents[^1].Level >= level)
            {
                parents.RemoveAt(parents.Count - 1);
            }

            var node = new OutlineNode(level, title, target);

            parents[^1].Children.Add(node);
            parents.Add(node);
        }

        var outlines = new PdfDictionary(Output);

        outlines.Elements.SetName("/Type", "/Outlines");
        Output.AddObject(outlines);
        WriteOutlineItems(outlines, root);
        Output.Catalog.Elements.SetReference("/Outlines", outlines);
    }

    private void WriteOutlineItems(PdfDictionary parent, OutlineNode node)
    {
        PdfDictionary previous = null;

        foreach (var child in node.Children)
        {
            var item = new PdfDictionary(Output);

            item.Elements.SetString("/Title", child.Title);
            item.Elements.SetReference("/Parent", parent);
            item.Elements["/Dest"] = Destination(child.Target);
            Output.AddObject(item);

            if (previous is null)
            {
                parent.Elements.SetReference("/First", item);
            }
            else
            {
                previous.Elements.SetReference("/Next", item);
                item.Elements.SetReference("/Prev", previous);
            }

            WriteOutlineItems(item, child);
            previous = item;
        }

        if (previous is not null)
        {
            parent.Elements.SetReference("/Last", previous);

            // Every level is shown open, so the count is every item under this one.
            parent.Elements.SetInteger("/Count", node.Descendants);
        }
    }

    /// <summary>
    /// A heading in the outline, and the headings under it.
    /// </summary>
    /// <param name="Level">The heading level.</param>
    /// <param name="Title">The heading text.</param>
    /// <param name="Target">Where the heading is.</param>
    private sealed record OutlineNode(int Level, string Title, PdfLayoutTarget Target)
    {
        /// <summary>
        /// Gets the headings under this one.
        /// </summary>
        public List<OutlineNode> Children { get; } = [];

        /// <summary>
        /// Gets the number of headings under this one, at every level.
        /// </summary>
        public int Descendants => Children.Count + Children.Sum(child => child.Descendants);
    }

    /// <summary>
    /// A table row laid out: its cells' paragraphs and its height.
    /// </summary>
    /// <param name="Row">The row.</param>
    /// <param name="Cells">Each cell's laid-out paragraphs.</param>
    /// <param name="Height">The height, padding and bottom border included.</param>
    /// <param name="BottomBorder">The width of the bottom border the row keeps room for.</param>
    /// <param name="TopBorder">The width of the top border the row keeps room for when it starts the table on a page.</param>
    private sealed record LaidRow(PdfLayoutRow Row, List<List<PdfLaidParagraph>> Cells, double Height, double BottomBorder, double TopBorder);

    /// <summary>
    /// A page being laid out.
    /// </summary>
    private sealed class PageState
    {
        /// <summary>
        /// Gets or sets the section the page belongs to.
        /// </summary>
        public PdfLayoutSection Section { get; set; }

        /// <summary>
        /// Gets or sets the index of the section.
        /// </summary>
        public int SectionIndex { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the page is its section's first.
        /// </summary>
        public bool IsFirstOfSection { get; set; }

        /// <summary>
        /// Gets or sets the zero-based index of the page in the document.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the number the page shows.
        /// </summary>
        public int Number { get; set; }

        /// <summary>
        /// Gets or sets the page width.
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Gets or sets the page height.
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// Gets or sets the written page, when the pass draws.
        /// </summary>
        public PdfPage Page { get; set; }

        /// <summary>
        /// Gets or sets the canvas the page is drawn with, when the pass draws.
        /// </summary>
        public PdfCanvas Canvas { get; set; }

        /// <summary>
        /// Gets the links on the page.
        /// </summary>
        public List<(PdfLayoutBox Box, PdfLayoutLink Link)> Links { get; } = [];
    }
}
