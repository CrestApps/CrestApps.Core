using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// Converts HTML into the lightweight markup <see cref="RichTextParser"/> understands.
/// <para>
/// Models asked for a "nicely formatted" document sometimes answer with HTML, even when the target is
/// a PDF or a Word file. Writing that string out verbatim puts raw tags in front of the reader. Rather
/// than rejecting it, the structure the HTML expresses — headings, lists, emphasis, tables — is
/// translated, and everything else is stripped so no markup can reach the page.
/// </para>
/// </summary>
public static partial class HtmlToMarkupConverter
{
    /// <summary>
    /// Determines whether a string carries HTML that should be converted rather than shown literally.
    /// <para>
    /// The check requires a recognized tag name, so ordinary prose containing a comparison such as
    /// <c>a &lt; b</c>, or Markdown containing an autolink, is not mistaken for markup and mangled.
    /// </para>
    /// </summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when the text should be treated as HTML.</returns>
    public static bool LooksLikeHtml(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && KnownTagRegex().IsMatch(value);
    }

    /// <summary>
    /// Converts HTML into the markup subset the parser reads.
    /// </summary>
    /// <param name="html">The HTML to convert.</param>
    /// <returns>The converted markup.</returns>
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html;

        // Anything inside these carries no reader-visible content, and their bodies are not markup, so
        // they are removed whole rather than having their tags stripped and their contents left behind.
        text = InvisibleElementRegex().Replace(text, "\n");
        text = CommentRegex().Replace(text, string.Empty);

        text = ConvertTables(text);
        text = ConvertHeadings(text);
        text = ConvertEmphasis(text);
        text = ConvertLists(text);
        text = ConvertBreaks(text);

        // Whatever structure remains is presentational, so the tags go and the text they wrapped stays.
        text = AnyTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        return Tidy(text);
    }

    private static string ConvertTables(string text)
    {
        return TableRegex().Replace(text, match =>
        {
            var builder = new StringBuilder("\n");
            var wroteSeparator = false;

            foreach (Match row in TableRowRegex().Matches(match.Value))
            {
                var cells = new List<string>();
                var isHeaderRow = false;

                foreach (Match cell in TableCellRegex().Matches(row.Value))
                {
                    if (cell.Groups["tag"].Value.Equals("th", StringComparison.OrdinalIgnoreCase))
                    {
                        isHeaderRow = true;
                    }

                    var content = AnyTagRegex().Replace(cell.Groups["content"].Value, " ");
                    content = WebUtility.HtmlDecode(content);

                    // A pipe inside a cell would split it into two, so it is neutralized.
                    cells.Add(WhitespaceRegex().Replace(content, " ").Replace("|", "/", StringComparison.Ordinal).Trim());
                }

                if (cells.Count == 0)
                {
                    continue;
                }

                builder.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");

                // A table is only recognized by the parser once a separator follows its first row, so
                // one is emitted after the header — or after the first row when there is no header.
                if (!wroteSeparator && (isHeaderRow || builder.Length > 1))
                {
                    builder.Append("| ").Append(string.Join(" | ", cells.Select(_ => "---"))).Append(" |\n");
                    wroteSeparator = true;
                }
            }

            return builder.Append('\n').ToString();
        });
    }

    private static string ConvertHeadings(string text)
    {
        return HeadingRegex().Replace(text, match =>
        {
            var level = int.Parse(match.Groups["level"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var content = AnyTagRegex().Replace(match.Groups["content"].Value, string.Empty).Trim();

            return content.Length == 0
                ? "\n"
                : $"\n\n{new string('#', level)} {content}\n\n";
        });
    }

    private static string ConvertEmphasis(string text)
    {
        text = StrongRegex().Replace(text, match => Wrap(match.Groups["content"].Value, "**"));
        text = EmphasisRegex().Replace(text, match => Wrap(match.Groups["content"].Value, "*"));
        text = CodeRegex().Replace(text, match => Wrap(match.Groups["content"].Value, "`"));

        return text;
    }

    private static string Wrap(string content, string marker)
    {
        var inner = AnyTagRegex().Replace(content, string.Empty).Trim();

        return inner.Length == 0
            ? string.Empty
            : marker + inner + marker;
    }

    private static string ConvertLists(string text)
    {
        return ListItemRegex().Replace(text, match =>
        {
            var content = AnyTagRegex().Replace(match.Groups["content"].Value, string.Empty);
            content = WhitespaceRegex().Replace(content, " ").Trim();

            return content.Length == 0
                ? string.Empty
                : $"\n- {content}";
        });
    }

    private static string ConvertBreaks(string text)
    {
        text = LineBreakRegex().Replace(text, "\n");
        text = BlockCloseRegex().Replace(text, "\n\n");
        text = HorizontalRuleRegex().Replace(text, "\n\n---\n\n");

        return text;
    }

    private static string Tidy(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length);
        var blankRun = 0;

        foreach (var line in lines)
        {
            // Non-breaking spaces survive decoding and would otherwise show as odd gaps.
            var trimmed = line.Replace(' ', ' ').TrimEnd();

            if (trimmed.Trim().Length == 0)
            {
                // Collapse the runs of blank lines that tag removal leaves behind; one is a paragraph
                // break, several are just vertical noise.
                if (++blankRun <= 1 && builder.Length > 0)
                {
                    builder.Append('\n');
                }

                continue;
            }

            blankRun = 0;
            builder.Append(trimmed.TrimStart()).Append('\n');
        }

        return builder.ToString().Trim('\n');
    }

    [GeneratedRegex(@"</?(?:html|head|body|div|span|p|br|hr|h[1-6]|ul|ol|li|table|thead|tbody|tfoot|tr|td|th|strong|b|em|i|u|a|img|code|pre|blockquote|section|article|header|footer|nav|main|style|script|font|center|small|sub|sup)\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex KnownTagRegex();

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex InvisibleElementRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 2000)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<table\b[^>]*>.*?</table\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr\b[^>]*>.*?</tr\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<(?<tag>t[dh])\b[^>]*>(?<content>.*?)</\k<tag>\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex(@"<h(?<level>[1-6])\b[^>]*>(?<content>.*?)</h\k<level>\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<(?:strong|b)\b[^>]*>(?<content>.*?)</(?:strong|b)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex StrongRegex();

    [GeneratedRegex(@"<(?:em|i)\b[^>]*>(?<content>.*?)</(?:em|i)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"<code\b[^>]*>(?<content>.*?)</code\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"<li\b[^>]*>(?<content>.*?)</li\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"</(?:p|div|ul|ol|table|blockquote|section|article|header|footer|main|pre)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex BlockCloseRegex();

    [GeneratedRegex(@"<hr\s*/?>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>", RegexOptions.None, 2000)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, 2000)]
    private static partial Regex WhitespaceRegex();
}
