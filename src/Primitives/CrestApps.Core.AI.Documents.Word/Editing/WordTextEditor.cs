using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Changes the text of paragraphs while keeping their formatting: replacing a paragraph's whole text, finding
/// and replacing text that runs across differently formatted runs, and reading a paragraph back as inline
/// Markdown — each as a tracked change when revisions are being tracked.
/// </summary>
/// <remarks>
/// A paragraph's text is spread over runs that each carry their own formatting, so a phrase to replace can
/// start halfway through one run and end in another. Runs are split at the edges of the phrase, the runs
/// inside it are removed — or kept as a tracked deletion — and the new text takes the formatting of the run
/// it replaces. Bookmarks, comment anchors and note references in a rewritten paragraph are kept, so a
/// table of contents, a comment or a footnote still points at it; so are fields, pictures and tabs inside a
/// phrase that is replaced.
/// </remarks>
internal static class WordTextEditor
{
    /// <summary>
    /// Replaces the whole text of a paragraph.
    /// </summary>
    /// <remarks>
    /// Bookmarks, comment anchors and note references are kept wherever they are in the paragraph — in a link, a
    /// tracked insertion or a content control too — and are never marked as deleted. Pictures, fields, content
    /// controls and links the new text does not have are removed with the old text, and the result says so.
    /// </remarks>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="markdown">The new text, with inline Markdown.</param>
    /// <param name="part">The part the paragraph is in.</param>
    /// <param name="revisions">The revision source when the change is tracked, or <see langword="null"/>.</param>
    /// <returns>
    /// A sentence naming what besides text was removed with the old text, such as a picture or a field, or
    /// <see langword="null"/> when only text was replaced.
    /// </returns>
    public static string ReplaceParagraph(Paragraph paragraph, string markdown, OpenXmlPart part, WordRevisions revisions)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var baseFormat = BaseFormatOf(paragraph);
        var content = paragraph.ChildElements.Where(child => child is not ParagraphProperties).ToList();

        // With tracking on, what is already a tracked deletion stays as it is, markers included.
        var keepers = content
            .SelectMany(child => child.Descendants().Prepend(child))
            .Where(IsKeeper)
            .Where(element => revisions is null || element.Ancestors<DeletedRun>().FirstOrDefault() is null)
            .ToList();

        foreach (var keeper in keepers)
        {
            keeper.Remove();
        }

        var replacement = new Paragraph();

        WordInlineWriter.AppendMarkdown(replacement, markdown ?? string.Empty, part, baseFormat);

        var dropped = DescribeDropped(content, replacement, revisions);

        foreach (var child in content)
        {
            if (child.Parent is null)
            {
                continue;
            }

            if (revisions is null || child is DeletedRun)
            {
                if (revisions is null)
                {
                    child.Remove();
                }

                continue;
            }

            foreach (var runToDelete in RunsOf(child))
            {
                revisions.Delete(runToDelete);
            }
        }

        var starts = keepers.Where(keeper => keeper is BookmarkStart or CommentRangeStart).ToList();
        var ends = keepers.Except(starts).ToList();

        foreach (var start in starts)
        {
            paragraph.Append(start);
        }

        foreach (var element in replacement.ChildElements.ToList())
        {
            element.Remove();
            paragraph.Append(revisions is null ? element : Track(element, revisions));
        }

        foreach (var end in ends)
        {
            paragraph.Append(end);
        }

        return dropped;
    }

    /// <summary>
    /// Finds text in the paragraphs of an element and replaces it, keeping the formatting of the text it
    /// replaces. Matches are found within one paragraph at a time; text inside fields is not changed.
    /// </summary>
    /// <param name="scope">The element whose paragraphs are searched: a body, a table or a paragraph.</param>
    /// <param name="find">The text to find.</param>
    /// <param name="replacement">The text to put in its place; empty to delete it.</param>
    /// <param name="matchCase">Whether case must match.</param>
    /// <param name="wholeWord">Whether only whole words match.</param>
    /// <param name="revisions">The revision source when the change is tracked, or <see langword="null"/>.</param>
    /// <param name="maxCount">The most matches to replace.</param>
    /// <returns>The number of matches replaced.</returns>
    public static int Replace(OpenXmlElement scope, string find, string replacement, bool matchCase, bool wholeWord, WordRevisions revisions, int maxCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrEmpty(find))
        {
            return 0;
        }

        var pattern = new Regex(
            (wholeWord ? "\\b" : string.Empty) + Regex.Escape(find) + (wholeWord ? "\\b" : string.Empty),
            matchCase ? RegexOptions.CultureInvariant : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

        var count = 0;
        var paragraphs = scope is Paragraph single ? [single] : scope.Descendants<Paragraph>().ToList();

        foreach (var paragraph in paragraphs)
        {
            if (count >= maxCount)
            {
                break;
            }

            var map = TextMap.Build(paragraph);
            var matches = pattern.Matches(map.Text).Take(maxCount - count).ToList();

            // From the last match back, so earlier offsets stay valid while later text is changed.
            for (var index = matches.Count - 1; index >= 0; index--)
            {
                ReplaceRange(map, matches[index].Index, matches[index].Length, replacement, revisions);
            }

            count += matches.Count;
        }

        return count;
    }

    /// <summary>
    /// Finds the runs that hold a piece of text in a paragraph, splitting runs at its edges, so the text can be
    /// formatted, linked or commented on by itself.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="find">The text.</param>
    /// <param name="matchCase">Whether case must match.</param>
    /// <param name="occurrence">Which occurrence, from 1.</param>
    /// <returns>The runs, in order, or an empty list when the text is not in the paragraph.</returns>
    public static List<Run> Isolate(Paragraph paragraph, string find, bool matchCase, int occurrence = 1)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (string.IsNullOrEmpty(find))
        {
            return [];
        }

        var map = TextMap.Build(paragraph);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = -1;

        for (var found = 0; found < Math.Max(1, occurrence); found++)
        {
            start = map.Text.IndexOf(find, start + 1, comparison);

            if (start < 0)
            {
                return [];
            }
        }

        return SplitRange(map, start, find.Length);
    }

    /// <summary>
    /// Returns every run of a paragraph's text, splitting none.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The runs holding visible text.</returns>
    public static List<Run> TextRuns(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        return [.. paragraph.Descendants<Run>().Where(run => run.Ancestors<DeletedRun>().FirstOrDefault() is null && run.Elements<Text>().Any())];
    }

    /// <summary>
    /// Reads a paragraph as inline Markdown: bold, italic, struck and code text, and links, so a model can
    /// rewrite it and the emphasis survives.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="part">The part the paragraph is in, which resolves its links.</param>
    /// <returns>The Markdown.</returns>
    public static string ToMarkdown(Paragraph paragraph, OpenXmlPart part)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var builder = new StringBuilder();

        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    builder.Append(RunMarkdown(run));

                    break;

                case Hyperlink link:
                    var text = string.Concat(link.Descendants<Run>().Select(RunMarkdown));
                    var target = link.Anchor?.Value is { } anchor
                        ? "#" + anchor
                        : link.Id?.Value is { } id ? part?.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == id)?.Uri?.ToString() : null;

                    builder.Append(target is null ? text : "[" + text + "](" + target + ")");

                    break;

                case InsertedRun or SimpleField or SdtRun or CustomXmlRun:
                    foreach (var run in child.Descendants<Run>())
                    {
                        builder.Append(RunMarkdown(run));
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string RunMarkdown(Run run)
    {
        var text = new StringBuilder();

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text value:
                    text.Append(value.Text);

                    break;

                case TabChar:
                    text.Append('\t');

                    break;

                case Break:
                    text.Append('\n');

                    break;
            }
        }

        if (text.Length == 0 || string.IsNullOrWhiteSpace(text.ToString()))
        {
            return text.ToString();
        }

        var properties = run.RunProperties;
        var result = text.ToString();
        var styleId = properties?.RunStyle?.Val?.Value;

        if (string.Equals(styleId, WordStyleSheet.InlineCode, StringComparison.OrdinalIgnoreCase) || Rendering.WordTextMeasurer.IsMonospace(properties?.RunFonts?.Ascii?.Value))
        {
            return "`" + result + "`";
        }

        if (properties?.Strike is { } strike && (strike.Val?.Value ?? true))
        {
            result = "~~" + result + "~~";
        }

        if (properties?.Italic is { } italic && (italic.Val?.Value ?? true))
        {
            result = "*" + result + "*";
        }

        if (properties?.Bold is { } bold && (bold.Val?.Value ?? true))
        {
            result = "**" + result + "**";
        }

        return result;
    }

    private static WordRunFormat BaseFormatOf(Paragraph paragraph)
    {
        // The new text takes the look of the paragraph's first run of text — its font, size and color — but not
        // the emphasis, which the Markdown now says.
        var first = TextRuns(paragraph).FirstOrDefault()?.RunProperties;

        if (first is null)
        {
            return null;
        }

        return new WordRunFormat
        {
            Font = first.RunFonts?.Ascii?.Value,
            Size = double.TryParse(first.FontSize?.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) ? halfPoints / 2 : null,
            Color = first.Color?.Val?.Value,
            StyleId = first.RunStyle?.Val?.Value is { } style && !string.Equals(style, WordStyleSheet.InlineCode, StringComparison.Ordinal) && !string.Equals(style, WordStyleSheet.Hyperlink, StringComparison.Ordinal) ? style : null,
        };
    }

    private static bool IsKeeper(OpenXmlElement element)
    {
        // What points at the paragraph from elsewhere: bookmarks, comment anchors, and comment and note references.
        return element is BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd ||
            (element is Run run && run.ChildElements.Any(child => child is CommentReference or FootnoteReference or EndnoteReference));
    }

    private static string DescribeDropped(List<OpenXmlElement> content, Paragraph replacement, WordRevisions revisions)
    {
        // What the old text held besides text, counted before it is removed; with tracking on, what is already a
        // tracked deletion is not counted again.
        var elements = content
            .Where(child => child.Parent is not null && (revisions is null || child is not DeletedRun))
            .SelectMany(child => child.Descendants().Prepend(child))
            .Where(element => revisions is null || element.Ancestors<DeletedRun>().FirstOrDefault() is null)
            .ToList();

        var parts = new List<string>();

        Count(parts, elements.Count(element => element is Drawing or Picture or EmbeddedObject), "picture or drawing", "pictures or drawings");
        Count(parts, elements.Count(element => element is SimpleField || (element is FieldChar character && character.FieldCharType?.Value == FieldCharValues.Begin)), "field", "fields");
        Count(parts, elements.Count(element => element is SdtRun), "content control", "content controls");
        Count(parts, Math.Max(0, elements.Count(element => element is Hyperlink) - replacement.Descendants<Hyperlink>().Count()), "link", "links");

        if (parts.Count == 0)
        {
            return null;
        }

        return "Removed with the old text: " + string.Join(", ", parts) + (revisions is null ? "." : " (as a tracked deletion).");
    }

    private static void Count(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
        {
            parts.Add(count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many));
        }
    }

    private static IEnumerable<Run> RunsOf(OpenXmlElement element)
    {
        if (element is Run run)
        {
            return [run];
        }

        return [.. element.Descendants<Run>().Where(candidate => candidate.Ancestors<DeletedRun>().FirstOrDefault() is null)];
    }

    private static OpenXmlElement Track(OpenXmlElement element, WordRevisions revisions)
    {
        if (element is Run run)
        {
            return revisions.Insert(run);
        }

        // A link keeps its own element; the runs inside it are what is inserted.
        foreach (var inner in element.Elements<Run>().ToList())
        {
            var insertion = revisions.Insert((Run)inner.CloneNode(true));

            inner.InsertBeforeSelf(insertion);
            inner.Remove();
        }

        return element;
    }

    private static void ReplaceRange(TextMap map, int start, int length, string replacement, WordRevisions revisions)
    {
        // Only the text goes: a field, a picture, a tab or a comment or note reference between the edges of the
        // phrase stays where it is, and the new text takes the place of the phrase's first run.
        var runs = TextRunsOf(SplitRange(map, start, length));

        if (runs.Count == 0)
        {
            return;
        }

        var first = runs[0];
        Run inserted = null;

        if (!string.IsNullOrEmpty(replacement))
        {
            inserted = new Run();

            if (first.RunProperties is not null)
            {
                inserted.Append(first.RunProperties.CloneNode(true));
            }

            WordInlineWriter.AppendText(inserted, replacement);
        }

        if (revisions is null)
        {
            if (inserted is not null)
            {
                first.InsertAfterSelf(inserted);
            }

            foreach (var run in runs)
            {
                run.Remove();
            }

            return;
        }

        DeletedRun firstDeletion = null;

        foreach (var run in runs)
        {
            var deletion = revisions.Delete(run);

            firstDeletion ??= deletion;
        }

        if (inserted is not null)
        {
            firstDeletion.InsertAfterSelf(revisions.Insert(inserted));
        }
    }

    private static List<Run> TextRunsOf(List<Run> runs)
    {
        var text = new List<Run>();
        var fieldDepth = 0;

        foreach (var run in runs)
        {
            // A run that is part of a field — its code, its result or one of its markers — is kept whole.
            var inField = fieldDepth > 0;

            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Begin:
                        fieldDepth++;
                        inField = true;

                        break;

                    case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End:
                        fieldDepth = Math.Max(0, fieldDepth - 1);
                        inField = true;

                        break;

                    case FieldChar or FieldCode:
                        inField = true;

                        break;
                }
            }

            if (!inField)
            {
                text.AddRange(SplitText(run));
            }
        }

        return text;
    }

    private static List<Run> SplitText(Run run)
    {
        // A run that holds text and something else — a tab, a picture, a reference — is split so that the text
        // can go without the rest.
        var children = run.ChildElements.Where(child => child is not RunProperties).ToList();

        if (!children.Any(child => child is Text))
        {
            return [];
        }

        if (children.All(child => child is Text))
        {
            return [run];
        }

        var pieces = new List<Run>();
        OpenXmlElement anchor = run;
        Run current = null;
        var currentIsText = false;

        foreach (var child in children)
        {
            var isText = child is Text;

            if (current is null || isText != currentIsText)
            {
                current = new Run();

                if (run.RunProperties is not null)
                {
                    current.Append(run.RunProperties.CloneNode(true));
                }

                anchor.InsertAfterSelf(current);
                anchor = current;
                currentIsText = isText;

                if (isText)
                {
                    pieces.Add(current);
                }
            }

            child.Remove();
            current.Append(child);
        }

        run.Remove();

        return pieces;
    }

    private static List<Run> SplitRange(TextMap map, int start, int length)
    {
        if (length <= 0 || start < 0 || start + length > map.Text.Length)
        {
            return [];
        }

        var (startText, startOffset) = map.Positions[start];
        var (endText, endOffset) = map.Positions[start + length - 1];
        var startRun = SplitBefore(startText, startOffset, out var startHolder);

        // When the range starts and ends in one text element, its end now sits in the part split off.
        if (ReferenceEquals(endText, startText))
        {
            endText = startHolder;
            endOffset -= startOffset;
        }

        var endRun = SplitAfter(endText, endOffset + 1);

        var runs = new List<Run>();
        var collecting = false;

        foreach (var run in map.Paragraph.Descendants<Run>().ToList())
        {
            if (ReferenceEquals(run, startRun))
            {
                collecting = true;
            }

            if (collecting && run.Ancestors<DeletedRun>().FirstOrDefault() is null)
            {
                runs.Add(run);
            }

            if (ReferenceEquals(run, endRun))
            {
                break;
            }
        }

        return runs;
    }

    private static Run SplitBefore(Text text, int offset, out Text holder)
    {
        var run = (Run)text.Parent;

        if (offset > 0)
        {
            holder = new Text(text.Text[offset..]) { Space = SpaceProcessingModeValues.Preserve };

            text.Text = text.Text[..offset];
            text.Space = SpaceProcessingModeValues.Preserve;
            text.InsertAfterSelf(holder);

            return MoveFrom(run, holder);
        }

        holder = text;

        return run.ChildElements.FirstOrDefault(child => child is not RunProperties) == text ? run : MoveFrom(run, text);
    }

    private static Run SplitAfter(Text text, int offset)
    {
        var run = (Run)text.Parent;

        if (offset < text.Text.Length)
        {
            var tail = new Text(text.Text[offset..]) { Space = SpaceProcessingModeValues.Preserve };

            text.Text = text.Text[..offset];
            text.Space = SpaceProcessingModeValues.Preserve;
            text.InsertAfterSelf(tail);
            MoveFrom(run, tail);

            return run;
        }

        var next = text.NextSibling();

        if (next is not null)
        {
            MoveFrom(run, next);
        }

        return run;
    }

    private static Run MoveFrom(Run run, OpenXmlElement child)
    {
        // The child and everything after it in the run move to a new run with the same formatting.
        var moved = new Run();

        if (run.RunProperties is not null)
        {
            moved.Append(run.RunProperties.CloneNode(true));
        }

        for (var current = child; current is not null;)
        {
            var next = current.NextSibling();

            current.Remove();
            moved.Append(current);
            current = next;
        }

        run.InsertAfterSelf(moved);

        return moved;
    }

    /// <summary>
    /// The visible text of a paragraph and, for each character, the text element and offset it comes from.
    /// </summary>
    private sealed class TextMap
    {
        private TextMap(Paragraph paragraph)
        {
            Paragraph = paragraph;
        }

        public Paragraph Paragraph { get; }

        public string Text { get; private set; }

        public List<(Text Element, int Offset)> Positions { get; } = [];

        public static TextMap Build(Paragraph paragraph)
        {
            var map = new TextMap(paragraph);
            var builder = new StringBuilder();
            var fieldDepth = 0;

            foreach (var element in paragraph.Descendants())
            {
                switch (element)
                {
                    case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Begin:
                        fieldDepth++;

                        break;

                    case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End:
                        fieldDepth = Math.Max(0, fieldDepth - 1);

                        break;

                    case Text text when fieldDepth == 0 && text.Parent is Run && text.Ancestors<DeletedRun>().FirstOrDefault() is null:
                        for (var offset = 0; offset < text.Text.Length; offset++)
                        {
                            map.Positions.Add((text, offset));
                        }

                        builder.Append(text.Text);

                        break;
                }
            }

            map.Text = builder.ToString();

            return map;
        }
    }
}
