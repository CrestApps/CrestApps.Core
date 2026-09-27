using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Breaks slide text into lines inside its box, the way PowerPoint lays it out closely enough that a line
/// ends at the same word and an overflowing box is recognised as overflowing.
/// </summary>
internal static class PresentationTextLayout
{
    /// <summary>
    /// The line height of single-spaced text as a multiple of its font size.
    /// </summary>
    public const double SingleLineFactor = 1.2;

    private static readonly double[] _fitScales = [1, 0.925, 0.85, 0.775, 0.7, 0.625, 0.55, 0.475, 0.4, 0.325, 0.25];

    /// <summary>
    /// Lays text out in a box.
    /// </summary>
    /// <param name="body">The text.</param>
    /// <param name="x">The left edge of the box, in points from the left of the slide.</param>
    /// <param name="y">The top edge of the box, in points from the top of the slide.</param>
    /// <param name="width">The width of the box in points.</param>
    /// <param name="height">The height of the box in points.</param>
    /// <param name="scale">An extra factor applied to every font size, used when fitting text.</param>
    /// <param name="lineReduction">The fraction taken off line spacing, used when fitting text.</param>
    /// <returns>The laid-out lines.</returns>
    public static TextLayoutResult Layout(
        PresentationTextBody body,
        double x,
        double y,
        double width,
        double height,
        double scale = 1,
        double lineReduction = 0)
    {
        var result = new TextLayoutResult();

        if (body is null)
        {
            return result;
        }

        var insetLeft = PresentationUnits.ToPoints(body.InsetLeft);
        var insetRight = PresentationUnits.ToPoints(body.InsetRight);
        var insetTop = PresentationUnits.ToPoints(body.InsetTop);
        var insetBottom = PresentationUnits.ToPoints(body.InsetBottom);
        var left = x + insetLeft;
        var contentWidth = Math.Max(1, width - insetLeft - insetRight);
        var cursor = 0d;
        var counters = new int[10];
        var smallest = double.MaxValue;
        var widest = 0d;

        for (var index = 0; index < body.Paragraphs.Count; index++)
        {
            var paragraph = body.Paragraphs[index];
            var level = Math.Clamp(paragraph.Level, 0, 8);

            // A deeper list restarts under each new parent, the way nested numbering reads.
            for (var deeper = level + 1; deeper < counters.Length; deeper++)
            {
                counters[deeper] = 0;
            }

            if (index > 0)
            {
                cursor += paragraph.SpaceBefore;
            }

            string markerText = null;

            if (paragraph.Bullet.IsVisible && paragraph.Runs.Any(run => !string.IsNullOrEmpty(run.Text)))
            {
                if (paragraph.Bullet.Kind == "number")
                {
                    counters[level]++;
                    markerText = FormatNumber(paragraph.Bullet.NumberScheme, paragraph.Bullet.StartAt + counters[level] - 1);
                }
                else
                {
                    markerText = string.IsNullOrEmpty(paragraph.Bullet.Character) ? "•" : paragraph.Bullet.Character;
                }
            }
            else
            {
                counters[level] = 0;
            }

            var lines = LayoutParagraph(paragraph, markerText, left, contentWidth, body.Wrap, scale, lineReduction);

            foreach (var line in lines)
            {
                line.Baseline += y + insetTop + cursor;
                cursor += line.Height;
                widest = Math.Max(widest, line.Left - left + line.Width);
                result.Lines.Add(line);

                foreach (var span in line.Spans)
                {
                    smallest = Math.Min(smallest, span.Style.Size);
                }
            }

            if (index < body.Paragraphs.Count - 1)
            {
                cursor += paragraph.SpaceAfter;
            }
        }

        result.RequiredHeight = cursor + insetTop + insetBottom;
        result.RequiredWidth = widest + insetLeft + insetRight;
        result.Overflows = result.RequiredHeight > height + 0.5;
        result.SmallestFontSize = smallest == double.MaxValue ? 0 : smallest;

        // The whole block moves with the anchor, so a middle-anchored box that overflows spills evenly above
        // and below it, as PowerPoint draws it.
        var slack = height - result.RequiredHeight;
        var offset = body.VerticalAnchor switch
        {
            "middle" => slack / 2,
            "bottom" => slack,
            _ => 0,
        };

        if (offset != 0)
        {
            foreach (var line in result.Lines)
            {
                line.Baseline += offset;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the largest font scale at which text fits its box, the way PowerPoint's shrink-on-overflow
    /// picks one.
    /// </summary>
    /// <param name="body">The text, at full size.</param>
    /// <param name="width">The width of the box in points.</param>
    /// <param name="height">The height of the box in points.</param>
    /// <returns>The font scale and the line spacing reduction, both 0 to 1; a scale of 1 when it fits.</returns>
    public static (double Scale, double LineReduction) FitScale(PresentationTextBody body, double width, double height)
    {
        foreach (var scale in _fitScales)
        {
            var reduction = scale >= 0.9 ? 0 : scale >= 0.7 ? 0.1 : 0.2;
            var result = Layout(body, 0, 0, width, height, scale, reduction);

            if (!result.Overflows)
            {
                return (scale, reduction);
            }
        }

        return (_fitScales[^1], 0.2);
    }

    /// <summary>
    /// Writes the label of a numbered paragraph.
    /// </summary>
    /// <param name="scheme">The numbering scheme, as the package names it.</param>
    /// <param name="number">The number.</param>
    /// <returns>The label, such as <c>3.</c>, <c>c)</c> or <c>IV.</c>.</returns>
    public static string FormatNumber(string scheme, int number)
    {
        var value = Math.Max(1, number);
        scheme ??= "arabicPeriod";

        var core = scheme switch
        {
            _ when scheme.StartsWith("alphaLc", StringComparison.Ordinal) => ToLetters(value).ToLowerInvariant(),
            _ when scheme.StartsWith("alphaUc", StringComparison.Ordinal) => ToLetters(value),
            _ when scheme.StartsWith("romanLc", StringComparison.Ordinal) => ToRoman(value).ToLowerInvariant(),
            _ when scheme.StartsWith("romanUc", StringComparison.Ordinal) => ToRoman(value),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        if (scheme.EndsWith("ParenBoth", StringComparison.Ordinal))
        {
            return "(" + core + ")";
        }

        if (scheme.EndsWith("ParenR", StringComparison.Ordinal))
        {
            return core + ")";
        }

        if (scheme.EndsWith("Plain", StringComparison.Ordinal))
        {
            return core;
        }

        return core + ".";
    }

    private static List<TextLayoutLine> LayoutParagraph(
        PresentationParagraph paragraph,
        string markerText,
        double left,
        double contentWidth,
        bool wrap,
        double scale,
        double lineReduction)
    {
        var lines = new List<TextLayoutLine>();
        var marginLeft = PresentationUnits.ToPoints(paragraph.MarginLeft);
        var indent = PresentationUnits.ToPoints(paragraph.Indent);
        var firstStart = marginLeft + indent;
        var otherStart = marginLeft;
        TextLayoutSpan marker = null;
        var markerX = left + Math.Max(0, firstStart);

        if (markerText is not null)
        {
            var styleRun = paragraph.Runs.FirstOrDefault(run => !string.IsNullOrEmpty(run.Text)) ?? paragraph.Runs[0];
            var markerStyle = CloneStyle(styleRun, styleRun.Size * scale * paragraph.Bullet.SizeRatio);
            markerStyle.Bold = paragraph.Bullet.Kind == "number" && styleRun.Bold;
            markerStyle.Underline = false;

            if (!string.IsNullOrEmpty(paragraph.Bullet.Color))
            {
                markerStyle.Color = paragraph.Bullet.Color;
            }

            if (!string.IsNullOrEmpty(paragraph.Bullet.Font) && paragraph.Bullet.Kind == "char" && !IsSymbolFont(paragraph.Bullet.Font))
            {
                markerStyle.Font = paragraph.Bullet.Font;
            }

            marker = new TextLayoutSpan
            {
                Text = markerText,
                Style = markerStyle,
                Width = PresentationTextMeasurer.Measure(markerText, markerStyle.Font, markerStyle.Size, markerStyle.Bold),
            };

            // A hanging bullet sits in the indent and the text starts at the margin; a bullet with no hang
            // pushes the first line's text along by its own width.
            firstStart = indent < 0
                ? Math.Max(marginLeft, firstStart + marker.Width + (styleRun.Size * scale * 0.25))
                : firstStart + marker.Width + (styleRun.Size * scale * 0.3);
        }

        var tokens = Tokenize(paragraph, scale);
        var current = new List<TextLayoutSpan>();
        var currentWidth = 0d;
        var lineStart = Math.Max(0, firstStart);
        var isFirstLine = true;

        void Flush()
        {
            TrimTrailingSpace(current, ref currentWidth);

            var line = BuildLine(paragraph, current, currentWidth, left, lineStart, contentWidth, scale, lineReduction);

            if (isFirstLine && marker is not null)
            {
                line.Marker = marker;
                line.MarkerX = markerX;
            }

            lines.Add(line);
            current = [];
            currentWidth = 0;
            lineStart = Math.Max(0, otherStart);
            isFirstLine = false;
        }

        foreach (var token in tokens)
        {
            if (token.IsBreak)
            {
                Flush();
                continue;
            }

            var available = contentWidth - lineStart;

            if (token.IsSpace)
            {
                if (current.Count == 0 && !isFirstLine)
                {
                    continue;
                }

                Append(current, token, ref currentWidth);
                continue;
            }

            if (wrap && current.Count > 0 && currentWidth + token.Width > available)
            {
                Flush();
                available = contentWidth - lineStart;
            }

            if (wrap && token.Width > available && token.Text.Length > 1)
            {
                // A word longer than the whole line is broken where it runs out of room, as PowerPoint does.
                foreach (var piece in SplitWord(token, available, available + lineStart))
                {
                    if (current.Count > 0 && currentWidth + piece.Width > contentWidth - lineStart)
                    {
                        Flush();
                    }

                    Append(current, piece, ref currentWidth);
                }

                continue;
            }

            Append(current, token, ref currentWidth);
        }

        Flush();

        return lines;
    }

    private static TextLayoutLine BuildLine(
        PresentationParagraph paragraph,
        List<TextLayoutSpan> spans,
        double width,
        double left,
        double lineStart,
        double contentWidth,
        double scale,
        double lineReduction)
    {
        var maxSize = spans.Count == 0 ? paragraph.EndSize * scale : spans.Max(span => span.Style.Size);
        double height;

        if (paragraph.LineSpacingPoints is > 0)
        {
            height = paragraph.LineSpacingPoints.Value * scale;
        }
        else
        {
            height = maxSize * SingleLineFactor * Math.Max(0.5, paragraph.LineSpacing - lineReduction);
        }

        var line = new TextLayoutLine
        {
            Spans = spans,
            Width = width,
            Height = height,
            Baseline = height - (maxSize * 0.25),
        };

        var available = contentWidth - lineStart;

        switch (paragraph.Alignment)
        {
            case "center":
                line.Anchor = "middle";
                line.X = left + lineStart + (available / 2);
                break;
            case "right":
                line.Anchor = "end";
                line.X = left + contentWidth;
                break;
            default:
                line.Anchor = "start";
                line.X = left + lineStart;
                break;
        }

        return line;
    }

    private static List<TextLayoutSpan> Tokenize(PresentationParagraph paragraph, double scale)
    {
        var tokens = new List<TextLayoutSpan>();

        foreach (var run in paragraph.Runs)
        {
            if (run.IsLineBreak)
            {
                tokens.Add(new TextLayoutSpan { IsBreak = true, Text = string.Empty, Style = run });
                continue;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var text = run.Capitalization is "all" or "small" ? run.Text.ToUpperInvariant() : run.Text;
            var size = run.Capitalization == "small" ? run.Size * scale * 0.8 : run.Size * scale;
            var style = CloneStyle(run, size);
            var builder = new StringBuilder();
            bool? inSpace = null;

            foreach (var character in text)
            {
                if (character is '\n' or '\v')
                {
                    AddToken(tokens, builder, style, inSpace == true);
                    tokens.Add(new TextLayoutSpan { IsBreak = true, Text = string.Empty, Style = style });
                    inSpace = null;
                    continue;
                }

                var isSpace = character == ' ' || character == '\t';

                if (inSpace is not null && inSpace != isSpace)
                {
                    AddToken(tokens, builder, style, inSpace.Value);
                }

                builder.Append(character == '\t' ? "    " : character);
                inSpace = isSpace;
            }

            AddToken(tokens, builder, style, inSpace == true);
        }

        return tokens;
    }

    private static void AddToken(List<TextLayoutSpan> tokens, StringBuilder builder, PresentationTextRun style, bool isSpace)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var text = builder.ToString();
        builder.Clear();

        tokens.Add(new TextLayoutSpan
        {
            Text = text,
            Style = style,
            Width = PresentationTextMeasurer.Measure(text, style.Font, style.Size, style.Bold),
            IsSpace = isSpace,
        });
    }

    private static void Append(List<TextLayoutSpan> spans, TextLayoutSpan token, ref double width)
    {
        width += token.Width;

        // Consecutive words in the same style are drawn as one span, which keeps the markup small and lets
        // the drawing surface space them with its own metrics.
        if (spans.Count > 0 && ReferenceEquals(spans[^1].Style, token.Style))
        {
            spans[^1].Text += token.Text;
            spans[^1].Width += token.Width;
            spans[^1].IsSpace = false;

            return;
        }

        spans.Add(new TextLayoutSpan
        {
            Text = token.Text,
            Style = token.Style,
            Width = token.Width,
            IsSpace = token.IsSpace,
        });
    }

    private static void TrimTrailingSpace(List<TextLayoutSpan> spans, ref double width)
    {
        while (spans.Count > 0)
        {
            var last = spans[^1];
            var trimmed = last.Text.TrimEnd(' ', '\t');

            if (trimmed.Length == last.Text.Length)
            {
                return;
            }

            var removed = PresentationTextMeasurer.Measure(last.Text[trimmed.Length..], last.Style.Font, last.Style.Size, last.Style.Bold);
            width -= removed;

            if (trimmed.Length == 0)
            {
                spans.RemoveAt(spans.Count - 1);
                continue;
            }

            last.Text = trimmed;
            last.Width -= removed;

            return;
        }
    }

    private static IEnumerable<TextLayoutSpan> SplitWord(TextLayoutSpan token, double firstAvailable, double fullWidth)
    {
        var builder = new StringBuilder();
        var width = 0d;
        var limit = Math.Max(firstAvailable, 1);

        foreach (var character in token.Text)
        {
            var characterWidth = PresentationTextMeasurer.Measure(character.ToString(), token.Style.Font, token.Style.Size, token.Style.Bold);

            if (builder.Length > 0 && width + characterWidth > limit)
            {
                yield return new TextLayoutSpan { Text = builder.ToString(), Style = token.Style, Width = width };

                builder.Clear();
                width = 0;
                limit = Math.Max(fullWidth, 1);
            }

            builder.Append(character);
            width += characterWidth;
        }

        if (builder.Length > 0)
        {
            yield return new TextLayoutSpan { Text = builder.ToString(), Style = token.Style, Width = width };
        }
    }

    private static PresentationTextRun CloneStyle(PresentationTextRun run, double size)
    {
        return new PresentationTextRun
        {
            Text = run.Text,
            Font = run.Font,
            Size = size,
            Bold = run.Bold,
            Italic = run.Italic,
            Underline = run.Underline,
            Strikethrough = run.Strikethrough,
            Color = run.Color,
            Highlight = run.Highlight,
            Capitalization = run.Capitalization,
            Baseline = run.Baseline,
            Link = run.Link,
            FieldType = run.FieldType,
        };
    }

    private static bool IsSymbolFont(string font)
    {
        return font.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) ||
            font.Contains("Symbol", StringComparison.OrdinalIgnoreCase) ||
            font.Contains("Webdings", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToLetters(int number)
    {
        var builder = new StringBuilder();
        var remaining = number;

        while (remaining > 0)
        {
            remaining--;
            builder.Insert(0, (char)('A' + (remaining % 26)));
            remaining /= 26;
        }

        return builder.ToString();
    }

    private static string ToRoman(int number)
    {
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] numerals = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        var remaining = Math.Min(number, 3999);

        for (var index = 0; index < values.Length; index++)
        {
            while (remaining >= values[index])
            {
                builder.Append(numerals[index]);
                remaining -= values[index];
            }
        }

        return builder.ToString();
    }
}
