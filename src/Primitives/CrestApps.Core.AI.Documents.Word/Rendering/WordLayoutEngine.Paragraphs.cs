using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Word.Fields;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <content>
/// Paragraphs: their text broken into tokens and lines, and lines placed on pages.
/// </content>
internal sealed partial class WordLayoutEngine
{
    // A paragraph longer than this is cut short, so one enormous run of text cannot hold up the layout; it is
    // far more than the pages a preview lays out can show.
    private const int MaxParagraphCharacters = 250_000;

    private static readonly HashSet<string> _understoodNamespaces = new(StringComparer.Ordinal)
    {
        "http://schemas.microsoft.com/office/word/2010/wordprocessingShape",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing",
        "http://schemas.microsoft.com/office/word/2010/wordml",
        "http://schemas.microsoft.com/office/drawing/2010/main",
    };

    private string _lastStyle;
    private double _lastSpaceAfter;
    private bool _lastContextual;

    private void FlowBlock(OpenXmlElement block)
    {
        if (_stopped || _page is null)
        {
            return;
        }

        switch (block)
        {
            case Paragraph paragraph:
                var box = BuildParagraph(paragraph, ColumnWidth, new LayoutContext(_package.MainPart, null));

                if (box.Format.KeepNext && !AtTopOfColumn)
                {
                    box.KeepWithHeight = MeasureKeepWith(paragraph);
                }

                PlaceParagraph(paragraph, box);

                break;

            case Table table:
                PlaceTable(table);

                break;

            case SdtBlock or CustomXmlBlock or AlternateContent when _depth >= MaxNesting:
                Issue("clipped", "Content nested too deeply is left out of the layout.", block);

                break;

            case SdtBlock control:
                _depth++;

                try
                {
                    foreach (var child in control.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
                    {
                        FlowBlock(child);
                    }
                }
                finally
                {
                    _depth--;
                }

                if (_layout.FirstPage.TryGetValue(control.SdtContentBlock?.FirstChild ?? control, out var first))
                {
                    _layout.FirstPage.TryAdd(control, first);
                    _layout.LastPage[control] = _page?.Index ?? first;
                }

                break;

            case CustomXmlBlock or AlternateContent:
                _depth++;

                try
                {
                    foreach (var child in block is AlternateContent alternate ? ChooseAlternate(alternate) : block.ChildElements)
                    {
                        FlowBlock(child);
                    }
                }
                finally
                {
                    _depth--;
                }

                break;
        }
    }

    /// <summary>
    /// Measures what a paragraph kept with the next one must share its page with: the next paragraph's first
    /// line, or all of it and what follows when it is kept with the next too, or the first row of a table.
    /// </summary>
    /// <param name="paragraph">The paragraph kept with the next.</param>
    /// <returns>The height in points.</returns>
    private double MeasureKeepWith(Paragraph paragraph)
    {
        const int MaxChain = 5;

        var height = 0d;
        var next = NextBlock(paragraph);

        for (var chained = 0; next is not null && chained < MaxChain; chained++)
        {
            switch (next)
            {
                case Table table:
                    return height + Measure(() =>
                    {
                        var context = new LayoutContext(_package.MainPart, _resolver.ResolveTable(table));
                        var rows = LayoutRows(table, [.. table.Elements<TableRow>()], 0, 1, ColumnWidth, context, out _, out _);

                        return rows.Count > 0 ? rows[0].Height : 0;
                    });

                case Paragraph following:
                    var box = Measure(() => BuildParagraph(following, ColumnWidth, new LayoutContext(_package.MainPart, null)));
                    var format = box.Format;

                    height += format.SpaceBefore;

                    // A chain of paragraphs kept with the next stays together with the first line after it.
                    if (format.KeepNext && chained + 1 < MaxChain && NextBlock(following) is not null)
                    {
                        height += box.Lines.Sum(line => line.Height) + format.SpaceAfter;
                        next = NextBlock(following);

                        continue;
                    }

                    return height + (box.Lines.Count > 0 ? box.Lines[0].Height : 0);

                default:
                    return height + 28;
            }
        }

        return height;
    }

    // The block after this one in the flow, looking into and out of content controls.
    private static OpenXmlElement NextBlock(OpenXmlElement block)
    {
        for (var steps = 0; block is not null && steps < MaxNesting; steps++)
        {
            var sibling = block.NextSibling();

            while (sibling is not null and not (Paragraph or Table or SdtBlock))
            {
                sibling = sibling.NextSibling();
            }

            if (sibling is SdtBlock control)
            {
                var inner = control.SdtContentBlock?.ChildElements.FirstOrDefault(child => child is Paragraph or Table);

                return inner ?? (OpenXmlElement)control;
            }

            if (sibling is not null)
            {
                return sibling;
            }

            // The end of a content control's content goes on after the control.
            block = block.Parent is SdtContentBlock content ? content.Parent : null;
        }

        return null;
    }

    // The branch of markup compatibility content this layout draws: the first choice whose namespaces it
    // understands, such as a Word 2010 shape, or the fallback.
    private static IEnumerable<OpenXmlElement> ChooseAlternate(AlternateContent alternate)
    {
        foreach (var choice in alternate.Elements<AlternateContentChoice>())
        {
            var requires = choice.Requires?.Value;

            if (string.IsNullOrWhiteSpace(requires))
            {
                continue;
            }

            var understood = requires.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(prefix => choice.LookupNamespace(prefix) is { } uri && _understoodNamespaces.Contains(uri));

            if (understood)
            {
                return choice.ChildElements;
            }
        }

        return alternate.GetFirstChild<AlternateContentFallback>()?.ChildElements ?? Enumerable.Empty<OpenXmlElement>();
    }

    private void PlaceParagraph(Paragraph paragraph, ParagraphBox box)
    {
        if (_stopped || _page is null)
        {
            return;
        }

        var format = box.Format;

        if (format.PageBreakBefore && _page.HasBodyContent && !AtTopOfColumn)
        {
            NewPage();

            if (_stopped)
            {
                return;
            }
        }

        var sameStyle = string.Equals(_lastStyle, format.StyleId, StringComparison.Ordinal);
        var spaceBefore = format.SpaceBefore;

        if (format.ContextualSpacing && sameStyle)
        {
            spaceBefore = 0;

            if (_lastContextual && !AtTopOfColumn)
            {
                _y -= _lastSpaceAfter;
            }
        }
        else if (!AtTopOfColumn)
        {
            // Adjacent spacing collapses to the larger of the two, as Word sets it.
            spaceBefore = Math.Max(0, spaceBefore - Math.Min(spaceBefore, _lastSpaceAfter));
        }

        var linesHeight = box.Lines.Sum(line => line.Height);

        if (format.KeepLines && !AtTopOfColumn && _y + spaceBefore + linesHeight > Bottom && linesHeight <= Bottom - _columnTop)
        {
            NextColumnOrPage(flowed: true);
        }
        else if (format.KeepNext && !AtTopOfColumn && box.KeepWithHeight > 0 && _y + spaceBefore + linesHeight + box.KeepWithHeight > Bottom && linesHeight + box.KeepWithHeight < Bottom - _columnTop)
        {
            NextColumnOrPage(flowed: true);
        }

        if (_stopped)
        {
            return;
        }

        foreach (var floating in box.Floating)
        {
            PlaceFloating(floating, paragraph);

            if (_stopped)
            {
                return;
            }
        }

        // Word drops the space before a paragraph at the top of a page or column the text flowed onto, but keeps
        // it on the first page, after a page or column break, and at the start of a section.
        if (!AtTopOfColumn || !_flowedToTop)
        {
            _y += spaceBefore;
        }

        var segmentTop = _y;
        var segmentStart = _page.Items.Count;

        void CloseSegment(bool first, bool last)
        {
            DecorateSegment(box, segmentTop, _y, first, last, paragraph, segmentStart);
        }

        var firstSegment = true;

        for (var index = 0; index < box.Lines.Count; index++)
        {
            var line = box.Lines[index];
            var footnotes = MeasureFootnotes(line);

            if (_y + line.Height + footnotes > Bottom && !AtTopOfColumn)
            {
                CloseSegment(firstSegment, last: false);
                firstSegment = false;
                NextColumnOrPage(flowed: true);

                if (_stopped)
                {
                    return;
                }

                segmentTop = _y;
                segmentStart = _page.Items.Count;
            }

            ReserveFootnotes(line);

            var x = ColumnLeft;

            foreach (var item in line.Items)
            {
                WordBox.Move(item, x, _y);
                AddToPage(item);
            }

            if (line.Width > ColumnWidth + 1)
            {
                Issue("overflow", $"A line of paragraph {DescribeBlock(paragraph)} is wider than the text column.", paragraph);
            }

            _y += line.Height;
            _page.BodyBottom = Math.Max(_page.BodyBottom, _y);
            Record(paragraph);

            if (line.Break == BreakKind.Page && index < box.Lines.Count - 1)
            {
                CloseSegment(firstSegment, last: false);
                firstSegment = false;
                NewPage();

                if (_stopped)
                {
                    return;
                }

                segmentTop = _y;
                segmentStart = _page.Items.Count;
            }
            else if (line.Break == BreakKind.Column && index < box.Lines.Count - 1)
            {
                CloseSegment(firstSegment, last: false);
                firstSegment = false;
                NextColumnOrPage();

                if (_stopped)
                {
                    return;
                }

                segmentTop = _y;
                segmentStart = _page.Items.Count;
            }
        }

        CloseSegment(firstSegment, last: true);

        _y += format.SpaceAfter;
        _lastSpaceAfter = format.SpaceAfter;
        _lastStyle = format.StyleId;
        _lastContextual = format.ContextualSpacing;

        // A page break at the very end of a paragraph keeps the paragraph mark on its page, as Word sets it, and
        // starts the next block on a new page.
        if (box.Lines.Count > 0 && box.Lines[^1].Break == BreakKind.Page)
        {
            NewPage();
        }
        else if (box.Lines.Count > 0 && box.Lines[^1].Break == BreakKind.Column)
        {
            NextColumnOrPage();
        }
    }

    // The page's picture budget is applied once the page is complete, over everything it draws.
    private void AddToPage(WordDrawItem item)
    {
        _page?.Items.Add(item);
    }

    private void DecorateSegment(ParagraphBox box, double top, double bottom, bool first, bool last, Paragraph paragraph, int insertAt)
    {
        var format = box.Format;

        if (format.Shading is null && format.Top is null && format.Bottom is null && format.Left is null && format.Right is null)
        {
            return;
        }

        var left = ColumnLeft + format.IndentLeft - (format.Left?.Space ?? 0) - 2;
        var right = ColumnLeft + ColumnWidth - format.IndentRight + (format.Right?.Space ?? 0) + 2;
        var decorations = new List<WordDrawItem>();

        if (format.Shading is not null && !string.Equals(format.Shading, "FFFFFF", StringComparison.OrdinalIgnoreCase))
        {
            decorations.Add(new WordRectItem { X = left, Y = top - 1, Width = right - left, Height = bottom - top + 2, Fill = format.Shading, Source = paragraph });
        }

        void Line(WordBorder border, double x1, double y1, double x2, double y2)
        {
            if (border is not null && border.Style != "none" && border.Width > 0)
            {
                decorations.Add(new WordLineItem { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Color = border.Color ?? "000000", Width = border.Width, Dotted = border.Style is "dotted" or "dashed", Source = paragraph });
            }
        }

        if (first)
        {
            Line(format.Top, left, top - (format.Top?.Space ?? 0) - 1, right, top - (format.Top?.Space ?? 0) - 1);
        }

        if (last)
        {
            Line(format.Bottom, left, bottom + (format.Bottom?.Space ?? 0) + 1, right, bottom + (format.Bottom?.Space ?? 0) + 1);
        }

        Line(format.Left, left, top, left, bottom);
        Line(format.Right, right, top, right, bottom);

        // Decorations sit behind the text they frame.
        _page.Items.InsertRange(Math.Clamp(insertAt, 0, _page.Items.Count), decorations);
    }

    private ParagraphBox BuildParagraph(Paragraph paragraph, double width, LayoutContext context)
    {
        var (format, run) = _resolver.Resolve(paragraph, context.Table);

        if (context.Conditions is { Count: > 0 })
        {
            foreach (var condition in context.Conditions)
            {
                _resolver.ApplyCondition(run, condition);
            }
        }

        var box = new ParagraphBox { Format = format, Element = paragraph };
        var tokens = new List<Token>();

        if (format.NumberId is { } numberId)
        {
            var marker = _lists.Next(numberId, format.NumberLevel, out var level);

            if (!string.IsNullOrEmpty(marker))
            {
                var markerFormat = _resolver.ResolveMarker(run, level);

                tokens.Add(new Token
                {
                    Kind = TokenKind.Text,
                    Text = marker,
                    Format = markerFormat,
                    Width = WordTextMeasurer.Measure(marker, markerFormat.Font, markerFormat.DrawnSize, markerFormat.Bold),
                    IsMarker = true,
                    Source = paragraph,
                });

                var suffix = level?.LevelSuffix?.Val?.InnerText ?? "tab";

                if (suffix == "tab")
                {
                    tokens.Add(new Token { Kind = TokenKind.Tab, Format = markerFormat, Source = paragraph, IsMarker = true });
                }
                else if (suffix == "space")
                {
                    tokens.Add(Space(" ", markerFormat, paragraph));
                }
            }
        }

        var state = new CollectState(context, run, box, paragraph, width);

        foreach (var child in paragraph.ChildElements)
        {
            Collect(child, state, tokens);
        }

        BreakLines(box, tokens, width, run);

        return box;
    }

    private void Collect(OpenXmlElement element, CollectState state, List<Token> tokens, string markup = null)
    {
        // Inline content nested past any real document's depth — hyperlinks in content controls in hyperlinks —
        // is left out rather than followed until the stack runs out.
        if (state.Depth >= MaxNesting || state.Characters >= MaxParagraphCharacters)
        {
            return;
        }

        state.Depth++;

        try
        {
            CollectElement(element, state, tokens, markup);
        }
        finally
        {
            state.Depth--;
        }
    }

    private void CollectElement(OpenXmlElement element, CollectState state, List<Token> tokens, string markup)
    {
        switch (element)
        {
            case ParagraphProperties:
            case BookmarkStart:
            case BookmarkEnd:
            case ProofError:
            case CommentRangeStart:
            case CommentRangeEnd:
                return;

            case Run run:
                CollectRun(run, state, tokens, markup);

                return;

            case AlternateContent alternate:
                foreach (var child in ChooseAlternate(alternate))
                {
                    Collect(child, state, tokens, markup);
                }

                return;

            case SimpleField simple:
                var type = WordFieldScanner.TypeOf(simple.Instruction?.Value);

                if (DynamicToken(type) is { } token)
                {
                    var format = _resolver.ResolveRun(state.Run, simple.GetFirstChild<Run>()?.RunProperties);

                    tokens.Add(Text(token, format, state.Paragraph, markup));

                    return;
                }

                break;

            case DeletedRun or MoveFromRun:
                if (_options.ShowMarkup)
                {
                    foreach (var child in element.ChildElements)
                    {
                        Collect(child, state, tokens, "deleted");
                    }
                }

                return;

            case InsertedRun or MoveToRun:
                foreach (var child in element.ChildElements)
                {
                    Collect(child, state, tokens, _options.ShowMarkup ? "inserted" : markup);
                }

                return;

            case DocumentFormat.OpenXml.Math.OfficeMath or DocumentFormat.OpenXml.Math.Paragraph:
                var mathText = element.InnerText;

                if (!string.IsNullOrWhiteSpace(mathText))
                {
                    var mathFormat = state.Run.Clone();

                    mathFormat.Font = "Cambria Math";
                    mathFormat.Italic = true;
                    AppendText(mathText, mathFormat, state, tokens, markup);
                }

                return;
        }

        foreach (var child in element.ChildElements)
        {
            Collect(child, state, tokens, markup);
        }
    }

    private void CollectRun(Run run, CollectState state, List<Token> tokens, string markup)
    {
        var format = _resolver.ResolveRun(state.Run, run.RunProperties);

        if (format.Hidden)
        {
            return;
        }

        _layout.Fonts.Add(format.Font);

        foreach (var child in run.ChildElements)
        {
            CollectRunChild(child, format, state, tokens, markup);
        }
    }

    private void CollectRunChild(OpenXmlElement child, WordResolvedRun format, CollectState state, List<Token> tokens, string markup)
    {
        if (state.Characters >= MaxParagraphCharacters || tokens.Count >= MaxParagraphCharacters)
        {
            return;
        }

        switch (child)
        {
            // Word writes its shapes and text boxes as a choice between the Word 2010 drawing and an older
            // fallback picture; one of them is drawn.
            case AlternateContent alternate when state.Depth < MaxNesting:
                state.Depth++;

                try
                {
                    foreach (var chosen in ChooseAlternate(alternate))
                    {
                        CollectRunChild(chosen, format, state, tokens, markup);
                    }
                }
                finally
                {
                    state.Depth--;
                }

                break;

            case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Begin:
                state.Fields.Push(new FieldState());

                break;

            case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Separate && state.Fields.Count > 0:
                var field = state.Fields.Peek();

                field.InResult = true;

                if (DynamicToken(WordFieldScanner.TypeOf(field.Instruction.ToString())) is { } dynamic)
                {
                    field.Replaced = true;
                    tokens.Add(Text(dynamic, format, state.Paragraph, markup));
                }

                break;

            case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End && state.Fields.Count > 0:
                var ended = state.Fields.Pop();

                // A field written without a result still shows its value.
                if (!ended.InResult && DynamicToken(WordFieldScanner.TypeOf(ended.Instruction.ToString())) is { } value)
                {
                    tokens.Add(Text(value, format, state.Paragraph, markup));
                }

                break;

            case FieldCode code when state.Fields.Count > 0 && !state.Fields.Peek().InResult:
                state.Fields.Peek().Instruction.Append(code.Text);

                break;

            case FieldCode:
            case DeletedFieldCode:
                break;

            case Text text:
                if (state.Fields.Count > 0 && (!state.Fields.Peek().InResult || state.Fields.Peek().Replaced))
                {
                    break;
                }

                AppendText(text.Text, format, state, tokens, markup);

                break;

            case DeletedText deleted when _options.ShowMarkup:
                AppendText(deleted.Text, format, state, tokens, "deleted");

                break;

            case TabChar:
            case PositionalTab:
                tokens.Add(new Token { Kind = TokenKind.Tab, Format = format, Source = state.Paragraph });

                break;

            case Break @break:
                var breakType = @break.Type?.InnerText;

                tokens.Add(new Token
                {
                    Kind = breakType == "page" ? TokenKind.PageBreak : breakType == "column" ? TokenKind.ColumnBreak : TokenKind.LineBreak,
                    Format = format,
                    Source = state.Paragraph,
                });

                break;

            case CarriageReturn:
                tokens.Add(new Token { Kind = TokenKind.LineBreak, Format = format, Source = state.Paragraph });

                break;

            case NoBreakHyphen:
                AppendText("-", format, state, tokens, markup);

                break;

            case SymbolChar symbol when symbol.Char?.Value is { Length: 4 } code && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var symbolValue):
                AppendText(symbolValue >= 0xF000 ? "•" : ((char)symbolValue).ToString(), format, state, tokens, markup);

                break;

            case FootnoteReference footnote when footnote.Id?.Value is { } footnoteId:
                var number = _footnoteNumbers.TryGetValue(footnoteId, out var existing) ? existing : _footnoteNumbers[footnoteId] = _footnoteNumbers.Count + 1;
                var superscript = format.Clone();

                superscript.VerticalPosition = 1;
                tokens.Add(Text(number.ToString(CultureInfo.InvariantCulture), superscript, state.Paragraph, markup));
                tokens[^1].FootnoteId = footnoteId;

                break;

            case EndnoteReference:
                var endnote = format.Clone();

                endnote.VerticalPosition = 1;
                tokens.Add(Text("i", endnote, state.Paragraph, markup));

                break;

            case FootnoteReferenceMark:
                var mark = format.Clone();

                mark.VerticalPosition = 1;
                tokens.Add(Text(state.FootnoteNumber ?? "*", mark, state.Paragraph, markup));

                break;

            case Drawing drawing:
                AddDrawing(drawing, state, tokens);

                break;

            case Picture picture:
                tokens.Add(LegacyPicture(picture, state));

                break;

            case EmbeddedObject:
                tokens.Add(Placeholder("Embedded object", 96, 48, state.Paragraph));

                break;
        }
    }

    private string DynamicToken(string fieldType)
    {
        var token = fieldType switch
        {
            "PAGE" => PageToken,
            "NUMPAGES" => PagesToken,
            "SECTIONPAGES" => SectionPagesToken,
            _ => null,
        };

        return token is not null && _pageFieldValues?.GetValueOrDefault(token) is { } value ? value : token;
    }

    private static void AppendText(string text, WordResolvedRun format, CollectState state, List<Token> tokens, string markup)
    {
        if (string.IsNullOrEmpty(text) || state.Characters >= MaxParagraphCharacters)
        {
            return;
        }

        var source = state.Paragraph;

        if (text.Length > MaxParagraphCharacters - state.Characters)
        {
            text = string.Concat(text.AsSpan(0, MaxParagraphCharacters - state.Characters), "…");
        }

        state.Characters += text.Length;

        if (format.Caps || format.SmallCaps)
        {
            text = text.ToUpperInvariant();
        }

        var start = 0;

        for (var index = 0; index <= text.Length; index++)
        {
            var boundary = index == text.Length || text[index] == ' ';

            if (!boundary)
            {
                continue;
            }

            if (index > start)
            {
                tokens.Add(Text(text[start..index], format, source, markup));
            }

            if (index < text.Length)
            {
                var spaceEnd = index;

                while (spaceEnd < text.Length && text[spaceEnd] == ' ')
                {
                    spaceEnd++;
                }

                tokens.Add(Space(text[index..spaceEnd], format, source));
                index = spaceEnd - 1;
                start = spaceEnd;
            }
        }
    }

    private static Token Text(string text, WordResolvedRun format, OpenXmlElement source, string markup = null)
    {
        var measured = text.Contains('\u0001', StringComparison.Ordinal) ? "99" : text;
        var size = format.SmallCaps ? format.DrawnSize * 0.8 : format.DrawnSize;

        return new Token
        {
            Kind = TokenKind.Text,
            Text = text,
            Format = format,
            Width = WordTextMeasurer.Measure(measured, format.Font, size, format.Bold),
            Source = source,
            Markup = markup,
        };
    }

    private static Token Space(string text, WordResolvedRun format, OpenXmlElement source)
    {
        return new Token
        {
            Kind = TokenKind.Space,
            Text = text,
            Format = format,
            Width = WordTextMeasurer.Measure(text, format.Font, format.DrawnSize, format.Bold),
            Source = source,
        };
    }

    private void BreakLines(ParagraphBox box, List<Token> tokens, double width, WordResolvedRun paragraphRun)
    {
        var format = box.Format;
        var right = width - format.IndentRight;
        var firstStart = format.IndentLeft + format.FirstLine;
        var line = new LineBuilder(Math.Max(0, firstStart));
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];

            switch (token.Kind)
            {
                case TokenKind.Space:
                    if (line.HasContent || line.X > format.IndentLeft + format.FirstLine + 0.01)
                    {
                        line.Add(token, token.Width);
                    }

                    index++;

                    continue;

                case TokenKind.Tab:
                    var next = NextTab(format, line.X, right, tokens, index, out var leader, out var stopAlignment);

                    if (next > right + 0.5 && line.HasContent)
                    {
                        FinishLine(box, line, right, paragraphRun, BreakKind.None, justify: true);
                        line = new LineBuilder(format.IndentLeft);
                        next = NextTab(format, line.X, right, tokens, index, out leader, out stopAlignment);
                    }

                    var tabToken = new Token { Kind = TokenKind.Tab, Format = token.Format, Source = token.Source, Text = leader, IsMarker = token.IsMarker };

                    line.Add(tabToken, Math.Max(0, next - line.X));
                    line.LastTabAlignment = stopAlignment;
                    index++;

                    continue;

                case TokenKind.LineBreak:
                case TokenKind.PageBreak:
                case TokenKind.ColumnBreak:
                    FinishLine(box, line, right, paragraphRun, token.Kind switch
                    {
                        TokenKind.PageBreak => BreakKind.Page,
                        TokenKind.ColumnBreak => BreakKind.Column,
                        _ => BreakKind.Line,
                    }, justify: false);

                    line = new LineBuilder(format.IndentLeft);
                    index++;

                    continue;
            }

            // A word is every text and object token up to the next space, tab or break.
            var end = index;
            var wordWidth = 0d;

            while (end < tokens.Count && tokens[end].Kind is TokenKind.Text or TokenKind.Inline)
            {
                wordWidth += tokens[end].Width;
                end++;
            }

            var available = right - line.X;

            // Text aligned to a right tab ends exactly at the margin; rounding must not push it onto a new line.
            if (wordWidth > available + 0.01 && line.HasContent && !tokens[index].IsMarker)
            {
                FinishLine(box, line, right, paragraphRun, BreakKind.None, justify: true);
                line = new LineBuilder(format.IndentLeft);
                available = right - line.X;
            }

            for (var position = index; position < end; position++)
            {
                var part = tokens[position];

                if (part.Kind == TokenKind.Text && part.Width > right - format.IndentLeft && right - format.IndentLeft > 20)
                {
                    // A word longer than the whole line is broken between characters.
                    foreach (var piece in SplitLong(part, right - line.X, right - format.IndentLeft))
                    {
                        if (piece.Width > right - line.X + 0.01 && line.HasContent)
                        {
                            FinishLine(box, line, right, paragraphRun, BreakKind.None, justify: false);
                            line = new LineBuilder(format.IndentLeft);
                        }

                        line.Add(piece, piece.Width);
                    }

                    continue;
                }

                line.Add(part, part.Width);
            }

            index = end;
        }

        // A paragraph that ends with a page break keeps its mark on the line of the break, as Word sets it, rather
        // than an empty line at the top of the next page.
        if (box.Lines.Count > 0 && box.Lines[^1].Break == BreakKind.Page && line.Tokens.All(entry => entry.Token.Kind == TokenKind.Space))
        {
            return;
        }

        FinishLine(box, line, right, paragraphRun, BreakKind.None, justify: false, last: true);
    }

    private static IEnumerable<Token> SplitLong(Token token, double firstWidth, double fullWidth)
    {
        var builder = new StringBuilder();
        var limit = Math.Max(firstWidth, 10);
        var width = 0d;

        // Widths add up character by character, so each piece is measured as it grows rather than again from
        // its start for every character.
        foreach (var character in token.Text)
        {
            var characterWidth = WordTextMeasurer.Measure(character.ToString(), token.Format.Font, token.Format.DrawnSize, token.Format.Bold);

            if (builder.Length > 0 && width + characterWidth > limit)
            {
                yield return Text(builder.ToString(), token.Format, token.Source, token.Markup);
                builder.Clear();
                width = 0;
                limit = fullWidth;
            }

            builder.Append(character);
            width += characterWidth;
        }

        if (builder.Length > 0)
        {
            yield return Text(builder.ToString(), token.Format, token.Source, token.Markup);
        }
    }

    private double NextTab(WordResolvedParagraph format, double x, double right, List<Token> tokens, int tabIndex, out string leader, out string alignment)
    {
        leader = null;
        alignment = "left";

        foreach (var stop in format.Tabs)
        {
            if (stop.Position > x + 0.5)
            {
                leader = stop.Leader switch
                {
                    "dot" or "middleDot" => ".",
                    "hyphen" => "-",
                    "underscore" or "heavy" => "_",
                    _ => null,
                };

                alignment = stop.Alignment;

                if (alignment is "right" or "center" or "decimal")
                {
                    var segment = 0d;

                    for (var index = tabIndex + 1; index < tokens.Count && tokens[index].Kind is TokenKind.Text or TokenKind.Space or TokenKind.Inline; index++)
                    {
                        segment += tokens[index].Width;
                    }

                    var start = alignment == "center" ? stop.Position - (segment / 2) : stop.Position - segment;

                    return Math.Max(x, start);
                }

                return stop.Position;
            }
        }

        // A list's hanging indent is a tab stop of its own.
        if (format.FirstLine < 0 && x < format.IndentLeft - 0.5)
        {
            return format.IndentLeft;
        }

        var tab = _defaultTab;

        return (Math.Floor((x + 0.5) / tab) + 1) * tab;
    }

    private static void FinishLine(ParagraphBox box, LineBuilder builder, double right, WordResolvedRun paragraphRun, BreakKind breakKind, bool justify, bool last = false)
    {
        var format = box.Format;

        // Trailing spaces neither count toward alignment nor are drawn.
        while (builder.Tokens.Count > 0 && builder.Tokens[^1].Token.Kind == TokenKind.Space)
        {
            builder.Tokens.RemoveAt(builder.Tokens.Count - 1);
        }

        double ascent = 0, descent = 0;

        foreach (var (token, _, _) in builder.Tokens)
        {
            if (token.Kind == TokenKind.Inline)
            {
                ascent = Math.Max(ascent, token.Height);
            }
            else
            {
                var size = token.Format.Size;
                var lineHeight = WordTextMeasurer.LineHeight(token.Format.Font, size);
                var tokenAscent = WordTextMeasurer.Ascent(token.Format.Font, size);

                ascent = Math.Max(ascent, tokenAscent);
                descent = Math.Max(descent, lineHeight - tokenAscent);
            }
        }

        if (ascent == 0 && descent == 0)
        {
            ascent = WordTextMeasurer.Ascent(paragraphRun.Font, paragraphRun.Size);
            descent = WordTextMeasurer.LineHeight(paragraphRun.Font, paragraphRun.Size) - ascent;
        }

        var natural = ascent + descent;
        var height = format.LineRule switch
        {
            "exact" => Math.Max(1, format.LineValue),
            "atLeast" => Math.Max(natural, format.LineValue),
            _ => natural * Math.Max(0.5, format.LineValue),
        };

        var baseline = height - descent;
        var contentEnd = builder.Tokens.Count == 0 ? builder.Start : builder.Tokens.Max(entry => entry.X + entry.Width);
        var free = Math.Max(0, right - contentEnd);
        var shift = format.Alignment switch
        {
            "center" => free / 2,
            "right" => free,
            _ => 0,
        };

        var spaces = builder.Tokens.Count(entry => entry.Token.Kind == TokenKind.Space);
        var extraPerSpace = format.Alignment == "both" && justify && !last && breakKind == BreakKind.None && spaces > 0 ? free / spaces : 0;
        var lineBox = new LineBox { Height = height, Break = breakKind, Width = contentEnd };
        var offset = 0d;

        // Words and the spaces between them in one format are drawn as one run of text, unless the line is
        // justified and every space stretches by itself.
        WordTextItem current = null;

        foreach (var (token, x, width) in builder.Tokens)
        {
            var left = x + shift + offset;

            if (token.Kind is not (TokenKind.Text or TokenKind.Space) || token.IsMarker || extraPerSpace > 0)
            {
                current = null;
            }

            switch (token.Kind)
            {
                case TokenKind.Space:
                    offset += extraPerSpace;
                    AddBackground(lineBox, token, left, width + extraPerSpace, baseline);

                    if (current is not null && ReferenceEquals(current.Format, token.Format) && token.Markup is null)
                    {
                        current.Text += token.Text;
                        current.Width += width;
                    }
                    else
                    {
                        current = null;
                    }

                    break;

                case TokenKind.Tab:
                    if (!string.IsNullOrEmpty(token.Text) && width > 8)
                    {
                        lineBox.Items.Add(new WordLineItem
                        {
                            X1 = left + 2,
                            Y1 = baseline - 1,
                            X2 = left + width - 2,
                            Y2 = baseline - 1,
                            Color = token.Format.Color ?? "000000",
                            Width = 0.8,
                            Dotted = token.Text != "_",
                            Source = token.Source,
                        });
                    }

                    break;

                case TokenKind.Inline:
                    foreach (var item in token.Object.Translate(left, baseline - token.Height))
                    {
                        lineBox.Items.Add(item);
                    }

                    break;

                default:
                    AddBackground(lineBox, token, left, width, baseline);

                    if (current is not null && ReferenceEquals(current.Format, token.Format) && token.Markup is null && !token.Text.Contains('', StringComparison.Ordinal))
                    {
                        current.Text += token.Text;
                        current.Width += width;

                        break;
                    }

                    var drawn = token.Format;

                    if (token.Markup is not null)
                    {
                        drawn = drawn.Clone();
                        drawn.Underline |= token.Markup == "inserted";
                        drawn.Strike |= token.Markup == "deleted";
                    }

                    var verticalShift = token.Format.VerticalPosition switch
                    {
                        1 => -token.Format.Size * 0.33,
                        -1 => token.Format.Size * 0.14,
                        _ => 0,
                    };

                    var textItem = new WordTextItem
                    {
                        X = left,
                        Baseline = baseline + verticalShift,
                        Width = width,
                        Text = token.Text,
                        Format = drawn,
                        MarkupColor = token.Markup switch
                        {
                            "inserted" => "1F6FEB",
                            "deleted" => "C00000",
                            _ => null,
                        },
                        Source = token.Source,
                    };

                    lineBox.Items.Add(textItem);

                    // A page-number placeholder is measured again once the number is known, so it stays alone.
                    current = token.Markup is null && !token.IsMarker && !token.Text.Contains('', StringComparison.Ordinal) ? textItem : null;

                    break;
            }

            if (token.FootnoteId >= 0)
            {
                lineBox.Footnotes.Add(token.FootnoteId);
            }
        }

        lineBox.Width = contentEnd + shift + offset;
        box.Lines.Add(lineBox);
    }

    private static void AddBackground(LineBox line, Token token, double left, double width, double baseline)
    {
        if (token.Format.Background is { } background && width > 0)
        {
            var size = token.Format.Size;

            line.Items.Add(new WordRectItem
            {
                X = left,
                Y = baseline - (size * 0.85),
                Width = width,
                Height = size * 1.1,
                Fill = background,
                Source = token.Source,
            });
        }
    }

    private double MeasureFootnotes(LineBox line)
    {
        var total = 0d;

        foreach (var id in line.Footnotes)
        {
            if (_pageFootnotes.Any(footnote => footnote.Id == id))
            {
                continue;
            }

            total += Measure(() => BuildFootnote(id)).Height;
        }

        return total > 0 && _pageFootnotes.Count == 0 ? total + 8 : total;
    }

    private void ReserveFootnotes(LineBox line)
    {
        foreach (var id in line.Footnotes)
        {
            if (_pageFootnotes.Any(footnote => footnote.Id == id))
            {
                continue;
            }

            var box = BuildFootnote(id);

            if (_pageFootnotes.Count == 0)
            {
                _footnoteReserve += 8;
            }

            _pageFootnotes.Add(new FootnoteBox(id, box));
            _footnoteReserve += box.Height;
        }
    }

    private WordBox BuildFootnote(long id)
    {
        var footnote = _package.MainPart.FootnotesPart?.Footnotes?.Elements<Footnote>().FirstOrDefault(item => item.Id?.Value == id);

        if (footnote is null)
        {
            return new WordBox();
        }

        var number = _footnoteNumbers.TryGetValue(id, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "*";

        return LayoutContainer(footnote.ChildElements, ColumnWidth, new LayoutContext(_package.MainPart.FootnotesPart, null) { FootnoteNumber = number });
    }

    private WordBox LayoutContainer(IEnumerable<OpenXmlElement> children, double width, LayoutContext context, List<(FloatingDrawing Drawing, double Top)> floating = null)
    {
        // Tables in tables in tables lay each other out recursively; past a depth no real document reaches, the
        // content is drawn as a placeholder rather than followed until the stack runs out.
        if (_depth >= MaxNesting)
        {
            Issue("clipped", "Content nested too deeply to lay out is drawn as a placeholder.", children.FirstOrDefault());

            return Placeholder("Nested content", Math.Max(4, width), 24, children.FirstOrDefault()).Object;
        }

        _depth++;

        try
        {
            return LayoutChildren(children, width, context, floating);
        }
        finally
        {
            _depth--;
        }
    }

    private WordBox LayoutChildren(IEnumerable<OpenXmlElement> children, double width, LayoutContext context, List<(FloatingDrawing Drawing, double Top)> floating)
    {
        var box = new WordBox();
        var y = 0d;
        string previousStyle = null;
        var previousAfter = 0d;

        foreach (var child in children)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    var built = BuildParagraph(paragraph, width, context);
                    var format = built.Format;
                    var before = format.ContextualSpacing && string.Equals(previousStyle, format.StyleId, StringComparison.Ordinal) ? 0 : Math.Max(0, format.SpaceBefore - Math.Min(format.SpaceBefore, previousAfter));

                    y += y > 0 ? before : 0;

                    var top = y;

                    foreach (var drawing in built.Floating)
                    {
                        floating?.Add((drawing, top));
                    }

                    foreach (var line in built.Lines)
                    {
                        foreach (var item in line.Items)
                        {
                            WordBox.Move(item, 0, y);
                            box.Items.Add(item);
                        }

                        y += line.Height;
                    }

                    if (format.Shading is not null)
                    {
                        box.Items.Insert(0, new WordRectItem { X = format.IndentLeft - 2, Y = top, Width = width - format.IndentLeft - format.IndentRight + 4, Height = y - top, Fill = format.Shading, Source = paragraph });
                    }

                    if (format.Bottom is { Style: not "none" } bottomBorder)
                    {
                        box.Items.Add(new WordLineItem { X1 = format.IndentLeft, Y1 = y + bottomBorder.Space + 1, X2 = width - format.IndentRight, Y2 = y + bottomBorder.Space + 1, Color = bottomBorder.Color ?? "000000", Width = bottomBorder.Width, Source = paragraph });
                    }

                    if (format.Top is { Style: not "none" } topBorder)
                    {
                        box.Items.Add(new WordLineItem { X1 = format.IndentLeft, Y1 = top - topBorder.Space - 1, X2 = width - format.IndentRight, Y2 = top - topBorder.Space - 1, Color = topBorder.Color ?? "000000", Width = topBorder.Width, Source = paragraph });
                    }

                    y += format.SpaceAfter;
                    previousAfter = format.SpaceAfter;
                    previousStyle = format.StyleId;

                    break;

                case Table table:
                    var tableBox = LayoutTableBox(table, width, context);

                    box.Items.AddRange(tableBox.Translate(0, y));
                    y += tableBox.Height;
                    previousAfter = 0;

                    break;

                case SdtBlock or AlternateContent:
                    var inner = LayoutContainer(
                        child is AlternateContent alternate ? ChooseAlternate(alternate) : ((SdtBlock)child).SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>(),
                        width,
                        context);

                    box.Items.AddRange(inner.Translate(0, y));
                    y += inner.Height;

                    break;
            }
        }

        // The space after the last paragraph does not make a container taller.
        box.Height = Math.Max(0, y - previousAfter);

        return box;
    }

    private static string DescribeBlock(OpenXmlElement element)
    {
        var id = OpenXml.Word.WordParagraphIds.Of(element);

        return id is null ? string.Empty : "[" + id + "]";
    }

    /// <summary>
    /// What ends a line.
    /// </summary>
    private enum BreakKind
    {
        None,
        Line,
        Page,
        Column,
    }

    /// <summary>
    /// What a token is.
    /// </summary>
    private enum TokenKind
    {
        Text,
        Space,
        Tab,
        LineBreak,
        PageBreak,
        ColumnBreak,
        Inline,
    }

    /// <summary>
    /// One unit of a paragraph's content: a word, a space, a tab, a break or an inline object.
    /// </summary>
    private sealed class Token
    {
        public TokenKind Kind { get; set; }

        public string Text { get; set; }

        public WordResolvedRun Format { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public WordBox Object { get; set; }

        public OpenXmlElement Source { get; set; }

        public string Markup { get; set; }

        public bool IsMarker { get; set; }

        public long FootnoteId { get; set; } = -1;
    }

    /// <summary>
    /// A line being filled.
    /// </summary>
    private sealed class LineBuilder
    {
        public LineBuilder(double start)
        {
            Start = start;
            X = start;
        }

        public double Start { get; }

        public double X { get; private set; }

        public string LastTabAlignment { get; set; }

        public List<(Token Token, double X, double Width)> Tokens { get; } = [];

        public bool HasContent => Tokens.Any(entry => entry.Token.Kind is TokenKind.Text or TokenKind.Inline);

        public void Add(Token token, double width)
        {
            Tokens.Add((token, X, width));
            X += width;
        }
    }

    /// <summary>
    /// A laid-out line, its items positioned from the paragraph's left edge and the line's top.
    /// </summary>
    private sealed class LineBox
    {
        public double Height { get; set; }

        public double Width { get; set; }

        public BreakKind Break { get; set; }

        public List<WordDrawItem> Items { get; } = [];

        public List<long> Footnotes { get; } = [];
    }

    /// <summary>
    /// A paragraph laid out into lines, ready to be placed.
    /// </summary>
    private sealed class ParagraphBox
    {
        public WordResolvedParagraph Format { get; set; }

        public Paragraph Element { get; set; }

        public List<LineBox> Lines { get; } = [];

        public List<FloatingDrawing> Floating { get; } = [];

        public double KeepWithHeight { get; set; }
    }

    /// <summary>
    /// A field being read while a paragraph is collected.
    /// </summary>
    private sealed class FieldState
    {
        public StringBuilder Instruction { get; } = new();

        public bool InResult { get; set; }

        public bool Replaced { get; set; }
    }

    /// <summary>
    /// What collecting a paragraph's tokens needs.
    /// </summary>
    private sealed class CollectState
    {
        public CollectState(LayoutContext context, WordResolvedRun run, ParagraphBox box, Paragraph paragraph, double width)
        {
            Context = context;
            Run = run;
            Box = box;
            Paragraph = paragraph;
            Width = width;
        }

        public LayoutContext Context { get; }

        public WordResolvedRun Run { get; }

        public ParagraphBox Box { get; }

        public Paragraph Paragraph { get; }

        public double Width { get; }

        public Stack<FieldState> Fields { get; } = new();

        public string FootnoteNumber => Context.FootnoteNumber;

        public int Depth { get; set; }

        public int Characters { get; set; }
    }
}
