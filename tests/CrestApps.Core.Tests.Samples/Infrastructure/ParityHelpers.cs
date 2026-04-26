using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Infrastructure;

/// <summary>
/// Helpers for asserting structural parity between the Mvc.Web and Blazor.Web UIs.
/// Mvc.Web is always the source of truth — Blazor.Web must match.
/// </summary>
public static class ParityHelpers
{
    /// <summary>
    /// Returns the visible text of every <c>.nav-link</c> within the document, in render order.
    /// Whitespace is normalized so that markup-formatting differences between the two UIs
    /// do not produce false negatives.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetTabLabelsAsync(IPage page, string scopeSelector = "body")
    {
        var locator = page.Locator($"{scopeSelector} .nav-link");
        var raw = await locator.AllInnerTextsAsync();

return raw.Select(NormalizeWhitespace).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>
    /// Returns the visible text of every form field <c>label</c> in render order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetFieldLabelsAsync(IPage page, string scopeSelector = "form")
    {
        var locator = page.Locator($"{scopeSelector} label");
        var raw = await locator.AllInnerTextsAsync();

return raw.Select(NormalizeWhitespace).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>
    /// Returns the visible text of every <c>button</c> and submit input in render order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetButtonLabelsAsync(IPage page, string scopeSelector = "body")
    {
        var locator = page.Locator($"{scopeSelector} button, {scopeSelector} input[type=submit]");
        var raw = await locator.AllInnerTextsAsync();

return raw.Select(NormalizeWhitespace).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>
    /// Returns the text of every heading (h1..h6) in render order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetHeadingsAsync(IPage page, string scopeSelector = "body")
    {
        var locator = page.Locator($"{scopeSelector} h1, {scopeSelector} h2, {scopeSelector} h3, {scopeSelector} h4, {scopeSelector} h5, {scopeSelector} h6");
        var raw = await locator.AllInnerTextsAsync();

return raw.Select(NormalizeWhitespace).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>
    /// Returns table column headers in render order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetTableHeadersAsync(IPage page, string scopeSelector = "body")
    {
        var locator = page.Locator($"{scopeSelector} table thead th");
        var raw = await locator.AllInnerTextsAsync();

return raw.Select(NormalizeWhitespace).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>
    /// Asserts both lists contain the same items in the same order. Produces a diff-style
    /// failure message identifying which UI is missing or out-of-order which item.
    /// </summary>
    public static void AssertSameOrdered(
        IReadOnlyList<string> mvc,
        IReadOnlyList<string> blazor,
        string label)
    {
        if (mvc.SequenceEqual(blazor, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var max = Math.Max(mvc.Count, blazor.Count);
        var diff = new System.Text.StringBuilder();
        diff.AppendLine($"{label} differ between Mvc.Web (source of truth) and Blazor.Web:");
        diff.AppendLine("  idx | mvc                                  | blazor");
        diff.AppendLine("  ----+--------------------------------------+--------------------------------------");

        for (var i = 0; i < max; i++)
        {
            var m = i < mvc.Count ? mvc[i] : "<missing>";
            var b = i < blazor.Count ? blazor[i] : "<missing>";
            var marker = string.Equals(m, b, StringComparison.OrdinalIgnoreCase) ? "  " : "* ";
            diff.AppendLine($"  {marker}{i,2} | {Truncate(m, 36),-36} | {Truncate(b, 36),-36}");
        }

        Assert.Fail(diff.ToString());
    }

    /// <summary>
    /// Asserts both lists contain the same items, ignoring order. Useful when DOM order
    /// can legitimately differ but the set of options must match (e.g. dropdown values).
    /// </summary>
    public static void AssertSameSet(
        IReadOnlyList<string> mvc,
        IReadOnlyList<string> blazor,
        string label)
    {
        var mvcSet = new HashSet<string>(mvc, StringComparer.OrdinalIgnoreCase);
        var blazorSet = new HashSet<string>(blazor, StringComparer.OrdinalIgnoreCase);

        var onlyInMvc = mvcSet.Except(blazorSet, StringComparer.OrdinalIgnoreCase).ToArray();
        var onlyInBlazor = blazorSet.Except(mvcSet, StringComparer.OrdinalIgnoreCase).ToArray();

        if (onlyInMvc.Length == 0 && onlyInBlazor.Length == 0)
        {
            return;
        }

        var diff = new System.Text.StringBuilder();
        diff.AppendLine($"{label} differ between Mvc.Web and Blazor.Web:");

        if (onlyInMvc.Length > 0)
        {
            diff.AppendLine("  Missing from Blazor (present in Mvc):");
            foreach (var item in onlyInMvc)
            {
                diff.AppendLine($"    - {item}");
            }
        }

        if (onlyInBlazor.Length > 0)
        {
            diff.AppendLine("  Extra in Blazor (not in Mvc):");
            foreach (var item in onlyInBlazor)
            {
                diff.AppendLine($"    + {item}");
            }
        }

        Assert.Fail(diff.ToString());
    }

    /// <summary>
    /// Asserts that an element with the given selector exists in the document.
    /// </summary>
    public static async Task AssertElementExistsAsync(IPage page, string selector, string description)
    {
        var count = await page.Locator(selector).CountAsync();
        Assert.True(count > 0, $"Expected element '{description}' (selector '{selector}') to exist, but it was not found.");
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
}
