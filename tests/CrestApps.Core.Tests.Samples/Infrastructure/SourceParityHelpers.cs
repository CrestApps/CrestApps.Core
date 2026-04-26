using System.Text.RegularExpressions;

namespace CrestApps.Core.Tests.Samples.Infrastructure;

/// <summary>
/// Source-level parity helpers that extract structural elements (headings, tab
/// labels, table headers, button labels) directly from <c>.cshtml</c> and
/// <c>.razor</c> templates. This avoids the need for a running browser, while
/// still asserting that the two UIs render the same conceptual structure.
/// </summary>
/// <remarks>
/// Mvc.Web is the source of truth — Blazor.Web must match. The extractors are
/// intentionally lenient (they normalize whitespace and strip Razor expressions
/// such as <c>@something</c> and <c>@(...)</c>) so that natural template
/// differences do not produce false negatives. We compare the static text that
/// both renderers emit verbatim.
/// </remarks>
public static class SourceParityHelpers
{
    private static readonly Regex s_scriptRegex = new(
        @"<script\b[^>]*>.*?</script>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex s_styleRegex = new(
        @"<style\b[^>]*>.*?</style>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns the source with <c><script></c> and <c><style></c>
    /// blocks stripped. These blocks frequently contain HTML-like template
    /// literals (e.g. <c>`<h6>${g.Category}</h6>`</c>) that would
    /// otherwise be matched by the structural extractors and produce false
    /// drift.
    /// </summary>
    public static string StripNonRenderedBlocks(string source)
    {
        var s = s_scriptRegex.Replace(source, " ");
        s = s_styleRegex.Replace(s, " ");

        return s;
    }

    private static readonly Regex s_headingRegex = new(
        @"<h([1-6])\b[^>]*>(?<inner>.*?)</h\1>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex s_thRegex = new(
        @"<th\b[^>]*>(?<inner>.*?)</th>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex s_navLinkRegex = new(
        @"<(?<tag>a|button)\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*\bclass\s*=\s*""[^""]*\bnav-link\b[^""]*""(?:[^>""']|""[^""]*""|'[^']*')*)>(?<inner>.*?)</\k<tag>>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex s_tabPaneRegex = new(
        @"\bdata-bs-target\s*=\s*""#(?<id>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_tabPaneIdHrefRegex = new(
        @"\bhref\s*=\s*""#(?<id>[^""]+)""[^>]*\bdata-bs-toggle\s*=\s*""(?:tab|pill)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_buttonRegex = new(
        @"<(?<tag>button|a)\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>(?<inner>.*?)</\k<tag>>|<input\b(?<inputAttrs>(?:[^>""']|""[^""]*""|'[^']*')*)/?>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex s_btnClassRegex = new(
        @"\bclass\s*=\s*""[^""]*\bbtn(?:\s+|-)[^""]*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_labelRegex = new(
        @"<label\b[^>]*>(?<inner>.*?)</label>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Strips razor/asp-net markup and inline HTML tags, returning the visible
    /// static text only. <c>@expr</c> and <c>@(...)</c> are removed because
    /// their rendered values are runtime data, not UI structure.
    /// </summary>
    public static string CleanInner(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw;

        // Drop Razor block expressions like @(expr) and @{...}
        s = Regex.Replace(s, @"@\([^()]*(?:\([^()]*\)[^()]*)*\)", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"@\{[^}]*\}", " ", RegexOptions.Singleline);

        // Drop Razor control-flow keywords (@if, @foreach, @while, @switch, @using, @do, @else)
        // along with their condition expression. The body itself remains and is processed normally.
        s = Regex.Replace(s, @"@(if|else\s+if|foreach|while|switch|using|for|do|else)\b\s*(\([^()]*(?:\([^()]*\)[^()]*)*\))?", " ", RegexOptions.Singleline);

        // Drop simple Razor expressions @Identifier(.Member)*[(...)]?
        s = Regex.Replace(s, @"@[A-Za-z_][A-Za-z0-9_\.]*(\([^)]*\))?", " ");

        // Drop stray `{` / `}` left over from razor block scaffolding.
        s = s.Replace("{", " ", StringComparison.Ordinal).Replace("}", " ", StringComparison.Ordinal);

        // Drop inline HTML tags but keep their inner text.
        s = Regex.Replace(s, @"<[^>]+>", " ");

        // Decode the few HTML entities we actually use in templates.
        s = s.Replace("&nbsp;", " ", StringComparison.Ordinal)
             .Replace("&amp;", "&", StringComparison.Ordinal)
             .Replace("<", "<", StringComparison.Ordinal)
             .Replace(">", ">", StringComparison.Ordinal)
             .Replace("&quot;", "\"", StringComparison.Ordinal);

        // Collapse whitespace.
        s = Regex.Replace(s, @"\s+", " ").Trim();

return s;
    }

    /// <summary>
    /// Returns headings (h1..h6) in the order they appear in the source,
    /// ignoring blank entries.
    /// </summary>
    public static IReadOnlyList<string> GetHeadings(string source, int level = 0)
        => s_headingRegex.Matches(StripNonRenderedBlocks(source))
            .Where(m => level == 0 || int.Parse(m.Groups[1].Value) == level)
            .Select(m => CleanInner(m.Groups["inner"].Value))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>
    /// Returns the visible text of every <c>nav-link</c> (Bootstrap tab/pill button) in source order.
    /// </summary>
    public static IReadOnlyList<string> GetTabLabels(string source)
        => s_navLinkRegex.Matches(StripNonRenderedBlocks(source))
            .Select(m => CleanInner(m.Groups["inner"].Value))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>
    /// Returns the ids referenced by tab-pane triggers (e.g. <c>data-bs-target="#basic-info"</c>
    /// or <c>href="#basic-info" data-bs-toggle="tab"</c>) — these are the tab-pane "contracts" that
    /// the page exposes, and must match between Mvc.Web and Blazor.Web.
    /// </summary>
    public static IReadOnlyList<string> GetTabPaneIds(string source)
    {
        var stripped = StripNonRenderedBlocks(source);
        var ids = new List<string>();
        ids.AddRange(s_tabPaneRegex.Matches(stripped).Select(m => m.Groups["id"].Value));
        ids.AddRange(s_tabPaneIdHrefRegex.Matches(stripped).Select(m => m.Groups["id"].Value));

return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Returns the visible text of every <c>th</c> cell in source order.
    /// </summary>
    public static IReadOnlyList<string> GetTableHeaders(string source)
        => s_thRegex.Matches(StripNonRenderedBlocks(source))
            .Select(m => CleanInner(m.Groups["inner"].Value))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>
    /// Returns the visible text of every <c>label</c> in source order.
    /// </summary>
    public static IReadOnlyList<string> GetFormLabels(string source)
        => s_labelRegex.Matches(StripNonRenderedBlocks(source))
            .Select(m => CleanInner(m.Groups["inner"].Value))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>
    /// Returns the visible text of every Bootstrap <c>btn</c> (button/anchor) in source order.
    /// We restrict the search to elements that visibly carry a Bootstrap <c>btn*</c> class so
    /// that ordinary table-cell anchors (e.g. clickable titles in a list) are excluded.
    /// </summary>
    public static IReadOnlyList<string> GetButtonLabels(string source)
    {
        var results = new List<string>();
        var stripped = StripNonRenderedBlocks(source);

        foreach (Match m in s_buttonRegex.Matches(stripped))
        {
            var attrs = m.Groups["attrs"].Success ? m.Groups["attrs"].Value : m.Groups["inputAttrs"].Value;

            if (string.IsNullOrEmpty(attrs) || !s_btnClassRegex.IsMatch(attrs))
            {
                continue;
            }

            string text;

            if (m.Groups["inner"].Success)
            {
                text = CleanInner(m.Groups["inner"].Value);
            }
            else
            {
                var valueMatch = Regex.Match(attrs, @"\bvalue\s*=\s*""(?<val>[^""]*)""", RegexOptions.IgnoreCase);
                text = valueMatch.Success ? CleanInner(valueMatch.Groups["val"].Value) : string.Empty;
            }

            if (text.Length > 0)
            {
                results.Add(text);
            }
        }

        return results;
    }

    /// <summary>
    /// Asserts that every entry from <paramref name="mvc"/> appears in <paramref name="blazor"/>
    /// (set semantics, case-insensitive). Use when render order may legitimately differ
    /// (e.g. Razor-component projection) but the conceptual set must match.
    /// </summary>
    public static void AssertContainsAllOf(
        IReadOnlyList<string> mvc,
        IReadOnlyList<string> blazor,
        string label,
        string pageDescription)
    {
        var blazorSet = new HashSet<string>(blazor, StringComparer.OrdinalIgnoreCase);
        var missing = mvc.Where(item => !blazorSet.Contains(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        var diff = new System.Text.StringBuilder();
        diff.AppendLine($"[{pageDescription}] {label}: items present in Mvc.Web but missing from Blazor.Web:");

        foreach (var item in missing)
        {
            diff.AppendLine($"  - {item}");
        }

        diff.AppendLine();
        diff.AppendLine("Mvc.Web (source of truth):");
        foreach (var item in mvc)
        {
            diff.AppendLine($"    • {item}");
        }

        diff.AppendLine();
        diff.AppendLine("Blazor.Web:");
        foreach (var item in blazor)
        {
            diff.AppendLine($"    • {item}");
        }

        Xunit.Assert.Fail(diff.ToString());
    }
}
