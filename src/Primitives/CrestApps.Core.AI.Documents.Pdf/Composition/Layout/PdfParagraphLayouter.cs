using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Breaks a paragraph into lines for a width: words wrap at spaces, a word wider than a line is broken
/// where the line is full, tabs move to their stops, and every line is aligned as the paragraph asks.
/// </summary>
internal static class PdfParagraphLayouter
{
    // A paragraph without tab stops of its own has one every 1.25 cm.
    private const double DefaultTabInterval = 35.43;

    /// <summary>
    /// Lays out a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="format">Its format, with everything its style and its container give.</param>
    /// <param name="width">The width of the paragraph's area, in points.</param>
    /// <param name="fieldText">Writes a field's value.</param>
    /// <returns>The laid-out paragraph.</returns>
    public static PdfLaidParagraph Layout(PdfLayoutParagraph paragraph, PdfLayoutParagraphFormat format, double width, Func<PdfLayoutField, string> fieldText)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(format);

        var borders = format.Borders;
        var laid = new PdfLaidParagraph
        {
            Format = format,
            SpaceBefore = Math.Max(0, format.SpaceBefore ?? 0),
            SpaceAfter = Math.Max(0, format.SpaceAfter ?? 0),
        };

        // A border takes its width and the distance it keeps from the text.
        if (borders.Top.IsDrawn())
        {
            laid.TopInset = (borders.Top.Width ?? 0.5) + (borders.DistanceFromTop ?? 0);
        }

        if (borders.Bottom.IsDrawn())
        {
            laid.BottomInset = (borders.Bottom.Width ?? 0.5) + (borders.DistanceFromBottom ?? 0);
        }

        var builder = new LineBuilder(format, width, laid);
        var text = new StringBuilder();

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case PdfLayoutText run:
                    text.Append(run.Text);
                    builder.AddText(run.Text, Style(inline, format), inline.Link);

                    break;

                case PdfLayoutField field:
                    builder.AddText(fieldText?.Invoke(field) ?? string.Empty, Style(inline, format), inline.Link);

                    break;

                case PdfLayoutBreak { IsTab: true }:
                    text.Append(' ');
                    builder.AddTab(Style(inline, format), inline.Link);

                    break;

                case PdfLayoutBreak:
                    text.Append(' ');
                    builder.BreakLine();

                    break;

                case PdfLayoutPicture picture:
                    builder.AddPicture(picture, inline.Link);

                    break;

                case PdfLayoutBookmark bookmark:
                    builder.AddBookmark(bookmark.Name);

                    break;
            }
        }

        builder.Finish();
        laid.Text = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return laid;
    }

    /// <summary>
    /// Resolves the font a piece of a paragraph is set in.
    /// </summary>
    /// <param name="inline">The piece.</param>
    /// <param name="format">The paragraph's format.</param>
    /// <returns>The style.</returns>
    public static PdfTextStyle Style(PdfLayoutInline inline, PdfLayoutParagraphFormat format)
    {
        var font = inline.Font.Over(format.Font);

        return new PdfTextStyle(
            PdfFontLibrary.Get(font.Name ?? PdfFontFamilies.Default, font.Bold == true, font.Italic == true),
            Math.Clamp(font.Size ?? 10, 1, 400),
            font.Color ?? new PdfColor(0, 0, 0),
            font.Underline == true);
    }

    /// <summary>
    /// Builds the lines one piece at a time.
    /// </summary>
    private sealed class LineBuilder
    {
        private readonly PdfLayoutParagraphFormat _format;
        private readonly double _width;
        private readonly PdfLaidParagraph _laid;
        private readonly PdfTextStyle _paragraphStyle;
        private readonly double _leftIndent;
        private readonly double _rightIndent;
        private readonly double _firstLineIndent;
        private PdfLaidLine _line;
        private double _x;
        private bool _wrapped;

        // A right or centred tab stop holds the text after it until the next tab or the end of the line.
        private PdfLayoutTabStop _pendingStop;
        private int _pendingFrom;
        private double _pendingX;
        private double _leaderFrom;
        private PdfTextStyle _leaderStyle;

        public LineBuilder(PdfLayoutParagraphFormat format, double width, PdfLaidParagraph laid)
        {
            _format = format;
            _width = width;
            _laid = laid;
            _paragraphStyle = Style(new PdfLayoutText(string.Empty), format);
            _leftIndent = format.LeftIndent ?? 0;
            _rightIndent = format.RightIndent ?? 0;
            _firstLineIndent = format.FirstLineIndent ?? 0;
            StartLine();
        }

        private double LineStart => _line.Left;

        private double Available => Math.Max(1, _width - _rightIndent - LineStart);

        public void AddText(string text, PdfTextStyle style, PdfLayoutLink link)
        {
            var word = new StringBuilder();

            foreach (var character in text ?? string.Empty)
            {
                switch (character)
                {
                    case ' ':
                        FlushWord(word, style, link);
                        AddSpace(style, link);

                        break;

                    case '\n':
                        FlushWord(word, style, link);
                        BreakLine();

                        break;

                    case '\t':
                        FlushWord(word, style, link);
                        AddTab(style, link);

                        break;

                    case '\r':
                        break;

                    default:
                        word.Append(character);

                        break;
                }
            }

            FlushWord(word, style, link);
        }

        public void AddTab(PdfTextStyle style, PdfLayoutLink link)
        {
            ClosePendingTab();

            var position = LineStart + _x;
            var stop = (_format.TabStops ?? []).OrderBy(candidate => candidate.Position).FirstOrDefault(candidate => candidate.Position > position + 0.01)
                ?? new PdfLayoutTabStop(Math.Floor((position / DefaultTabInterval) + 1) * DefaultTabInterval);

            var target = stop.Position - LineStart;

            if (target > Available + 0.01)
            {
                // A stop past the line's end is not reached: the tab is a space.
                AddSpace(style, link);

                return;
            }

            if (stop.Alignment is PdfLayoutAlignment.Right or PdfLayoutAlignment.Center)
            {
                _pendingStop = stop;
                _pendingFrom = _line.Items.Count;
                _pendingX = _x;
                _leaderFrom = _x;
                _leaderStyle = style;

                return;
            }

            if (stop.DotLeader)
            {
                AddLeader(_x, target, style);
            }

            _x = target;
        }

        public void AddPicture(PdfLayoutPicture picture, PdfLayoutLink link)
        {
            if (_line.Items.Count > 0 && _x + picture.Width > Available)
            {
                BreakLine(wrapped: true);
            }

            _line.Items.Add(new PdfLaidItem
            {
                X = _x,
                Width = picture.Width,
                Image = picture.Image,
                ImageHeight = picture.Height,
                Link = link,
            });

            _x += picture.Width;
        }

        public void AddBookmark(string name)
        {
            _line.Bookmarks.Add(name);
        }

        public void BreakLine(bool wrapped = false)
        {
            FinishLine(isLast: false, justify: wrapped);
            _wrapped = wrapped;
            StartLine();
        }

        public void Finish()
        {
            FinishLine(isLast: true, justify: false);
        }

        private void StartLine()
        {
            _line = new PdfLaidLine
            {
                Left = _leftIndent,
            };

            if (_laid.Lines.Count == 0)
            {
                _line.Left += _firstLineIndent;
            }

            _x = 0;
        }

        private void AddSpace(PdfTextStyle style, PdfLayoutLink link)
        {
            // A line that wrapped does not start with the space it wrapped at.
            if (_wrapped && _line.Items.Count == 0)
            {
                return;
            }

            var width = style.Font.Measure(" ", style.Size);

            _line.Items.Add(new PdfLaidItem
            {
                X = _x,
                Width = width,
                Text = " ",
                IsSpace = true,
                Font = style.Font,
                Size = style.Size,
                Color = style.Color,
                Underline = style.Underline,
                Link = link,
            });

            _x += width;
        }

        private void FlushWord(StringBuilder word, PdfTextStyle style, PdfLayoutLink link)
        {
            if (word.Length == 0)
            {
                return;
            }

            var text = word.ToString();

            word.Clear();

            var width = style.Font.Measure(text, style.Size);

            if (_x + width > Available + 0.01 && _line.Items.Any(item => !item.IsSpace) && _pendingStop is null)
            {
                BreakLine(wrapped: true);
            }

            if (width <= Available + 0.01 || _pendingStop is not null)
            {
                AddWord(text, width, style, link);

                return;
            }

            // A word wider than the whole line is broken where the line is full, the way a long address is.
            var part = new StringBuilder();
            var partWidth = 0d;

            foreach (var code in style.Font.Encode(text))
            {
                var advance = code.Advance * style.Size;

                if (part.Length > 0 && _x + partWidth + advance > Available)
                {
                    AddWord(part.ToString(), partWidth, style, link);
                    BreakLine(wrapped: true);
                    part.Clear();
                    partWidth = 0;
                }

                part.Append(code.Text);
                partWidth += advance;
            }

            if (part.Length > 0)
            {
                AddWord(part.ToString(), partWidth, style, link);
            }
        }

        private void AddWord(string text, double width, PdfTextStyle style, PdfLayoutLink link)
        {
            _line.Items.Add(new PdfLaidItem
            {
                X = _x,
                Width = width,
                Text = text,
                Font = style.Font,
                Size = style.Size,
                Color = style.Color,
                Underline = style.Underline,
                Link = link,
            });

            _x += width;
        }

        private void AddLeader(double from, double to, PdfTextStyle style)
        {
            var dot = style.Font.Measure(".", style.Size);

            if (dot <= 0)
            {
                return;
            }

            // The dots start where the text ends and fill as many whole dots as reach the tab stop.
            var count = (int)Math.Floor((to - from) / dot);

            if (count < 1)
            {
                return;
            }

            _line.Items.Add(new PdfLaidItem
            {
                X = from,
                Width = count * dot,
                Text = new string('.', count),
                Font = style.Font,
                Size = style.Size,
                Color = style.Color,
            });
        }

        private void ClosePendingTab()
        {
            if (_pendingStop is null)
            {
                return;
            }

            var stop = _pendingStop;
            var target = stop.Position - LineStart;
            var segment = _x - _pendingX;
            var start = stop.Alignment == PdfLayoutAlignment.Right
                ? target - segment
                : target - (segment / 2);

            // Text longer than the room before its stop starts where the tab was.
            var shift = Math.Max(0, start - _pendingX);
            var moved = _line.Items.Skip(_pendingFrom).ToList();

            foreach (var item in moved)
            {
                item.X += shift;
            }

            _pendingStop = null;

            if (stop.DotLeader)
            {
                AddLeader(_leaderFrom, _pendingX + shift, _leaderStyle);
            }

            _x += shift;
        }

        private void FinishLine(bool isLast, bool justify)
        {
            ClosePendingTab();

            while (_line.Items.Count > 0 && _line.Items[^1].IsSpace)
            {
                _line.Items.RemoveAt(_line.Items.Count - 1);
            }

            var contentWidth = _line.Items.Count == 0
                ? 0
                : _line.Items.Max(item => item.X + item.Width);

            var alignment = _format.Alignment ?? PdfLayoutAlignment.Left;
            var extra = Available - contentWidth;

            if (alignment == PdfLayoutAlignment.Justify && justify && !isLast && extra > 0)
            {
                Justify(extra);
            }
            else if (alignment is PdfLayoutAlignment.Right or PdfLayoutAlignment.Center && extra > 0)
            {
                var offset = alignment == PdfLayoutAlignment.Right
                    ? extra
                    : extra / 2;

                foreach (var item in _line.Items)
                {
                    item.X += offset;
                }
            }

            Measure();
            _laid.Lines.Add(_line);
        }

        private void Justify(double extra)
        {
            var spaces = _line.Items.Count(item => item.IsSpace);

            if (spaces == 0)
            {
                return;
            }

            var each = extra / spaces;
            var shift = 0d;

            foreach (var item in _line.Items)
            {
                item.X += shift;

                if (item.IsSpace)
                {
                    item.Width += each;
                    shift += each;
                }
            }
        }

        /// <summary>
        /// Sets the line's height and baseline: the tallest font's single spacing, times the paragraph's
        /// factor, with the extra space below the text and the baseline the deepest descent above the bottom
        /// of the single-spaced line.
        /// </summary>
        private void Measure()
        {
            var paragraphFont = _paragraphStyle.Font;
            var paragraphSize = _paragraphStyle.Size;
            var single = 0d;
            var descent = 0d;

            foreach (var item in _line.Items)
            {
                if (item.Image is not null)
                {
                    // A picture stands on the baseline, as tall as the text it replaces is high.
                    single = Math.Max(single, (paragraphFont.LineSpacing * paragraphSize) - (paragraphFont.Ascent * paragraphSize) + item.ImageHeight);
                    descent = Math.Max(descent, -paragraphFont.Descent * paragraphSize);

                    continue;
                }

                single = Math.Max(single, item.Font.LineSpacing * item.Size);
                descent = Math.Max(descent, -item.Font.Descent * item.Size);
            }

            // An empty line is as tall as the paragraph's font makes it.
            if (_line.Items.Count == 0)
            {
                single = paragraphFont.LineSpacing * paragraphSize;
                descent = -paragraphFont.Descent * paragraphSize;
            }

            switch (_format.LineSpacingRule ?? PdfLayoutLineSpacing.Single)
            {
                case PdfLayoutLineSpacing.Exactly:
                    {
                        // An exact height keeps the baseline where the paragraph's font puts it, scaled to that height.
                        var height = Math.Max(0.1, _format.LineSpacing ?? single);

                        _line.Height = height;
                        _line.Baseline = height * (1 + paragraphFont.Descent);

                        break;
                    }

                case PdfLayoutLineSpacing.Multiple:
                    _line.Height = single * Math.Max(0.1, _format.LineSpacing ?? 1);
                    _line.Baseline = single - descent;

                    break;

                default:
                    _line.Height = single;
                    _line.Baseline = single - descent;

                    break;
            }
        }
    }
}
