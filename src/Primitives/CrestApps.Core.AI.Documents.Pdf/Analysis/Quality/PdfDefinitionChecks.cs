using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Compares a composed document's definition with the file it rendered to: page setup per section, running
/// heads and page numbers, cover page, table of contents, headings, pictures, charts and content kept within
/// the text width.
/// </summary>
internal static partial class PdfDefinitionChecks
{
    private const int MaxListed = 6;
    private const double SizeTolerance = 2;

    /// <summary>
    /// Returns whether every page is a size the definition asks for, so a mix of sizes is intended.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="pages">The inspected pages.</param>
    /// <param name="options">The host's composition defaults.</param>
    /// <returns><see langword="true"/> when every page matches a size of the definition.</returns>
    public static bool SizesMatch(PdfDocumentDefinition definition, IReadOnlyList<PdfPageInspection> pages, PdfCompositionOptions options)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pages);

        var expected = ExpectedGeometries(definition, options ?? new PdfCompositionOptions());

        return pages.All(page => expected.Any(geometry => Matches(geometry, page)));
    }

    /// <summary>
    /// Runs the comparison.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="pages">Every page of the rendered file, inspected.</param>
    /// <param name="options">The host's composition defaults.</param>
    /// <param name="warnings">What the renderer reported it could not do as asked.</param>
    public static void Run(
        PdfCheckList checks,
        PdfDocumentDefinition definition,
        IReadOnlyList<PdfPageInspection> pages,
        PdfCompositionOptions options,
        IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pages);

        options ??= new PdfCompositionOptions();

        if (pages.Count == 0)
        {
            return;
        }

        var texts = pages.ToDictionary(page => page.Number, page => Squash(PdfPageText.GetPlainText(page.Page)));
        var (coverPages, tocPages) = FindFrontMatter(definition, pages, texts);
        var bodyPages = pages.Where(page => page.Number > coverPages + tocPages.Count).ToList();

        CheckPageSetup(checks, definition, pages, options);
        CheckCover(checks, definition, pages, texts);
        CheckTableOfContents(checks, definition, tocPages, texts);
        CheckRunningHeads(checks, definition, bodyPages);
        CheckHeadings(checks, definition, bodyPages, texts);
        CheckPictures(checks, definition, bodyPages, texts);
        CheckTextWidth(checks, definition, bodyPages, options);

        if (warnings is { Count: > 0 })
        {
            checks.Warn("Renderer warnings", "Rendering the document reported: " + PdfCheckList.List(warnings, MaxListed), "Correct the definition as each warning says.");
        }
    }

    /// <summary>
    /// Resolves the page geometry a section is laid out with, the way the composer does.
    /// </summary>
    /// <param name="section">The section's page setup, or <see langword="null"/> for the document's.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="options">The host's composition defaults.</param>
    /// <returns>The page size and margins, in points.</returns>
    public static (double Width, double Height, double Left, double Right, double Top, double Bottom) ResolveGeometry(
        PdfPageSetupDefinition section,
        PdfDocumentDefinition definition,
        PdfCompositionOptions options)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(options);

        var document = definition.PageSetup ?? new PdfPageSetupDefinition();
        section ??= new PdfPageSetupDefinition();

        var customWidth = section.WidthMm;
        var customHeight = section.HeightMm;

        if (section.Size is null)
        {
            customWidth ??= document.WidthMm;
            customHeight ??= document.HeightMm;
        }

        double width;
        double height;

        if (customWidth is > 20 && customHeight is > 20)
        {
            width = PdfPageSizes.FromMillimetres(Math.Min(customWidth.Value, 5000));
            height = PdfPageSizes.FromMillimetres(Math.Min(customHeight.Value, 5000));
        }
        else if (!PdfPageSizes.TryGet(section.Size ?? document.Size ?? options.DefaultPageSize, out width, out height) &&
            !PdfPageSizes.TryGet(options.DefaultPageSize, out width, out height))
        {
            PdfPageSizes.TryGet("A4", out width, out height);
        }

        var orientation = (section.Orientation ?? document.Orientation)?.Trim().ToLowerInvariant();

        if ((orientation == "landscape" && width < height) || (orientation == "portrait" && width > height))
        {
            (width, height) = (height, width);
        }

        var fallback = PdfPageSizes.FromMillimetres(Math.Clamp(options.DefaultMarginMm, 0, 80));

        double Margin(double? sectionValue, double? documentValue)
        {
            var value = sectionValue ?? documentValue;

            return value is >= 0
                ? PdfPageSizes.FromMillimetres(Math.Min(value.Value, 100))
                : fallback;
        }

        var left = Margin(section.MarginLeftMm, document.MarginLeftMm);
        var right = Margin(section.MarginRightMm, document.MarginRightMm);
        var top = Margin(section.MarginTopMm, document.MarginTopMm);
        var bottom = Margin(section.MarginBottomMm, document.MarginBottomMm);

        if (width - left - right < 72 || height - top - bottom < 72)
        {
            (left, right, top, bottom) = (fallback, fallback, fallback, fallback);
        }

        return (width, height, left, right, top, bottom);
    }

    private static List<(string Label, (double Width, double Height, double Left, double Right, double Top, double Bottom) Geometry)> ExpectedGeometries(
        PdfDocumentDefinition definition,
        PdfCompositionOptions options)
    {
        var expected = new List<(string Label, (double Width, double Height, double Left, double Right, double Top, double Bottom) Geometry)>();

        if (definition.CoverPage?.Enabled == true || definition.TableOfContents?.Enabled == true)
        {
            expected.Add(("the cover and contents", ResolveGeometry(null, definition, options)));
        }

        var sections = definition.Sections ?? [];

        for (var index = 0; index < sections.Count; index++)
        {
            var label = string.IsNullOrWhiteSpace(sections[index]?.Id)
                ? string.Create(CultureInfo.InvariantCulture, $"section {index + 1}")
                : $"section {sections[index].Id}";

            expected.Add((label, ResolveGeometry(sections[index]?.PageSetup, definition, options)));
        }

        if (expected.Count == 0)
        {
            expected.Add(("the document", ResolveGeometry(null, definition, options)));
        }

        return expected;
    }

    private static bool Matches((string Label, (double Width, double Height, double Left, double Right, double Top, double Bottom) Geometry) expected, PdfPageInspection page)
    {
        var (width, height) = PdfLayoutChecks.DisplaySize(page);

        return Math.Abs(expected.Geometry.Width - width) <= SizeTolerance && Math.Abs(expected.Geometry.Height - height) <= SizeTolerance;
    }

    private static (int CoverPages, List<int> TocPages) FindFrontMatter(
        PdfDocumentDefinition definition,
        IReadOnlyList<PdfPageInspection> pages,
        Dictionary<int, string> texts)
    {
        var cover = definition.CoverPage?.Enabled == true && pages.Count > 1
            ? 1
            : 0;

        var toc = new List<int>();

        if (definition.TableOfContents?.Enabled != true)
        {
            return (cover, toc);
        }

        var title = string.IsNullOrWhiteSpace(definition.TableOfContents.Title)
            ? Squash("Contents")
            : Squash(definition.TableOfContents.Title);
        var start = pages.Select(page => page.Number)
            .Where(number => number > cover && number <= cover + 3)
            .FirstOrDefault(number => texts[number].Contains(title, StringComparison.Ordinal));

        if (start == 0)
        {
            return (cover, toc);
        }

        toc.Add(start);

        // The contents run on while pages keep their dot leaders.
        for (var number = start + 1; number <= pages.Count && texts.TryGetValue(number, out var text) && text.Contains("......", StringComparison.Ordinal); number++)
        {
            toc.Add(number);
        }

        return (cover, toc);
    }

    private static void CheckPageSetup(PdfCheckList checks, PdfDocumentDefinition definition, IReadOnlyList<PdfPageInspection> pages, PdfCompositionOptions options)
    {
        var expected = ExpectedGeometries(definition, options);
        var unmatchedSections = expected
            .Where(geometry => !pages.Any(page => Matches(geometry, page)))
            .Select(geometry => $"{geometry.Label} should be {PdfPageSizes.Describe(geometry.Geometry.Width, geometry.Geometry.Height)}")
            .ToList();

        var strayPages = pages.Where(page => !expected.Any(geometry => Matches(geometry, page))).ToList();

        if (unmatchedSections.Count == 0 && strayPages.Count == 0)
        {
            var sizes = expected
                .Select(geometry => PdfPageSizes.Describe(geometry.Geometry.Width, geometry.Geometry.Height))
                .Distinct(StringComparer.Ordinal);

            checks.Pass("Page setup", "Every page has a size and orientation the definition asks for (" + string.Join(", ", sizes) + ").");

            return;
        }

        var parts = new List<string>();

        if (unmatchedSections.Count > 0)
        {
            parts.Add("no page has the size " + PdfCheckList.List(unmatchedSections, MaxListed));
        }

        if (strayPages.Count > 0)
        {
            parts.Add($"{PdfCheckList.Pages(strayPages.Select(page => page.Number), "is", "are")} {PdfCheckList.List(strayPages.Select(PdfLayoutChecks.DescribeSize).Distinct(StringComparer.Ordinal), 3)}, which no section asks for");
        }

        checks.Fail("Page setup", "The rendered pages do not follow the page setup: " + string.Join("; ", parts) + ".", "Check the page setup of the document and its sections with format_pdf.");
    }

    private static void CheckCover(PdfCheckList checks, PdfDocumentDefinition definition, IReadOnlyList<PdfPageInspection> pages, Dictionary<int, string> texts)
    {
        if (definition.CoverPage?.Enabled != true)
        {
            return;
        }

        var title = definition.CoverPage.Title ?? definition.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            if (pages[0].IsBlank)
            {
                checks.Fail("Cover page", "A cover page is enabled, but page 1 is blank.", "Give the cover a title, subtitle or logo with format_pdf.");
            }
            else
            {
                checks.Pass("Cover page", "Page 1 is the cover.");
            }

            return;
        }

        if (texts[pages[0].Number].Contains(Squash(title), StringComparison.Ordinal))
        {
            checks.Pass("Cover page", $"Page 1 is the cover, titled {PdfCheckList.Quote(title)}.");

            return;
        }

        checks.Fail("Cover page", $"A cover page is enabled, but page 1 does not show its title {PdfCheckList.Quote(title)}.", "Preview page 1 with preview_pdf and correct the cover with format_pdf.");
    }

    private static void CheckTableOfContents(PdfCheckList checks, PdfDocumentDefinition definition, List<int> tocPages, Dictionary<int, string> texts)
    {
        if (definition.TableOfContents?.Enabled != true)
        {
            return;
        }

        if (tocPages.Count == 0)
        {
            checks.Fail("Table of contents", "A table of contents is enabled, but no page near the start shows its title.", "Preview the first pages with preview_pdf and check the table of contents settings with format_pdf.");

            return;
        }

        var depth = Math.Clamp(definition.TableOfContents.Depth ?? 2, 1, 4);
        var headings = Headings(definition).Where(heading => heading.Level <= depth).ToList();
        var contents = string.Concat(tocPages.Select(number => texts[number]));
        var listed = headings.Count(heading => contents.Contains(Squash(heading.Text), StringComparison.Ordinal));

        var finding = string.Create(CultureInfo.InvariantCulture, $"The table of contents is on {PdfCheckList.Pages(tocPages)} and lists {listed} of the {headings.Count} heading(s) up to level {depth}.");

        if (listed < headings.Count)
        {
            checks.Warn("Table of contents", finding + " Some headings are missing from it.", "Preview the contents with preview_pdf; re-render after the headings are final.");

            return;
        }

        checks.Pass("Table of contents", finding);
    }

    private static void CheckRunningHeads(PdfCheckList checks, PdfDocumentDefinition definition, List<PdfPageInspection> bodyPages)
    {
        if (bodyPages.Count == 0)
        {
            return;
        }

        var slots = new List<(string Label, string Template, bool Top, bool SkipFirst)>();

        void AddSlots(PdfHeaderFooterDefinition slot, string name, bool top)
        {
            if (slot is null)
            {
                return;
            }

            var skipFirst = slot.ShowOnFirstPage == false;

            foreach (var (position, text) in new[] { ("left", slot.Left), ("centre", slot.Center), ("right", slot.Right) })
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    slots.Add(($"{name} ({position}) {PdfCheckList.Quote(text)}", text, top, skipFirst));
                }
            }
        }

        AddSlots(definition.Header, "header", top: true);
        AddSlots(definition.Footer, "footer", top: false);

        var numbers = definition.PageNumbers;

        if (numbers?.Enabled == true)
        {
            var template = string.IsNullOrWhiteSpace(numbers.Template)
                ? "Page {page} of {pages}"
                : numbers.Template;
            var top = (numbers.Position ?? string.Empty).Trim().StartsWith("header", StringComparison.OrdinalIgnoreCase) ||
                (numbers.Position ?? string.Empty).Trim().StartsWith("top", StringComparison.OrdinalIgnoreCase);

            slots.Add(($"page numbers {PdfCheckList.Quote(template)}", template, top, numbers.SkipFirstPage == true));
        }

        if (slots.Count == 0)
        {
            return;
        }

        var missing = new List<string>();
        var partial = new List<string>();
        var complete = new List<string>();
        var start = numbers?.StartAt is > 0
            ? numbers.StartAt.Value
            : 1;
        var arabic = string.IsNullOrWhiteSpace(numbers?.Format) || numbers.Format.Trim() == "1";
        var bands = new Dictionary<(int Page, bool Top), string>();

        foreach (var (label, template, top, skipFirst) in slots)
        {
            var expectedPages = skipFirst
                ? bodyPages.Skip(1).ToList()
                : bodyPages;

            if (expectedPages.Count == 0)
            {
                continue;
            }

            var absent = new List<int>();

            for (var index = 0; index < expectedPages.Count; index++)
            {
                var page = expectedPages[index];
                var bodyIndex = bodyPages.IndexOf(page);
                int? number = arabic
                    ? start + bodyIndex
                    : null;

                if (!bands.TryGetValue((page.Number, top), out var band))
                {
                    band = Squash(BandText(page, top));
                    bands[(page.Number, top)] = band;
                }

                if (!IsMatch(band, BuildPattern(template, definition, number)))
                {
                    absent.Add(page.Number);
                }
            }

            if (absent.Count == expectedPages.Count)
            {
                missing.Add(label);
            }
            else if (absent.Count > 0)
            {
                partial.Add($"{label} is missing on {PdfCheckList.Pages(absent)}");
            }
            else
            {
                complete.Add(label);
            }
        }

        if (missing.Count > 0)
        {
            checks.Fail("Running heads and page numbers", "Not found on any body page: " + PdfCheckList.List(missing, MaxListed) + ".", "Preview a body page with preview_pdf and check the header, footer and page number settings with format_pdf.");
        }

        if (partial.Count > 0)
        {
            checks.Warn("Running heads and page numbers", PdfCheckList.List(partial, MaxListed) + ".", "Preview those pages with preview_pdf; content running into the header or footer area can hide it.");
        }

        if (missing.Count == 0 && partial.Count == 0)
        {
            checks.Pass("Running heads and page numbers", string.Create(CultureInfo.InvariantCulture, $"Found on every body page ({bodyPages.Count}): ") + PdfCheckList.List(complete, MaxListed) + ".");
        }
    }

    private static void CheckHeadings(PdfCheckList checks, PdfDocumentDefinition definition, List<PdfPageInspection> bodyPages, Dictionary<int, string> texts)
    {
        var headings = Headings(definition);

        if (headings.Count == 0)
        {
            return;
        }

        var body = string.Concat(bodyPages.Select(page => texts[page.Number]));
        var missing = headings.Where(heading => !body.Contains(Squash(heading.Text), StringComparison.Ordinal)).ToList();

        if (missing.Count == 0)
        {
            checks.Pass("Headings", string.Create(CultureInfo.InvariantCulture, $"All {headings.Count} heading(s) of the definition appear in the rendered pages."));

            return;
        }

        checks.Fail(
            "Headings",
            string.Create(CultureInfo.InvariantCulture, $"{missing.Count} of {headings.Count} heading(s) do not appear in the rendered pages: ") + PdfCheckList.List(missing.Select(heading => PdfCheckList.Quote(heading.Text)), MaxListed) + ".",
            "Check those blocks with preview_pdf; a heading lost in rendering usually has markup the renderer did not understand.");
    }

    private static void CheckPictures(PdfCheckList checks, PdfDocumentDefinition definition, List<PdfPageInspection> bodyPages, Dictionary<int, string> texts)
    {
        var blocks = (definition.Sections ?? []).SelectMany(section => section?.Blocks ?? []).Where(block => block is not null).ToList();
        var imageBlocks = blocks.Count(block => PdfMigraDocBuilder.NormalizeType(block) == PdfBlockTypes.Image && !string.IsNullOrWhiteSpace(block.Image?.Source));
        var charts = blocks.Where(block => PdfMigraDocBuilder.NormalizeType(block) == PdfBlockTypes.Chart).ToList();

        if (imageBlocks > 0)
        {
            var drawn = bodyPages.Sum(page => page.Images.Count);

            if (drawn < imageBlocks)
            {
                checks.Fail(
                    "Pictures",
                    string.Create(CultureInfo.InvariantCulture, $"The definition places {imageBlocks} picture(s), but the body pages draw only {drawn}; a picture that cannot be found is left out."),
                    "Check each image block's source (an uploaded file, an asset id or a figure) and re-add the missing pictures.");
            }
            else
            {
                checks.Pass("Pictures", string.Create(CultureInfo.InvariantCulture, $"All {imageBlocks} picture(s) of the definition are drawn."));
            }
        }

        if (charts.Count == 0)
        {
            return;
        }

        var graphicPages = bodyPages.Count(page => page.Paths.Count(path => path.IsVisible) >= 8);
        var titled = charts.Where(chart => !string.IsNullOrWhiteSpace(chart.Chart?.Title)).ToList();
        var body = string.Concat(bodyPages.Select(page => texts[page.Number]));
        var missingTitles = titled.Where(chart => !body.Contains(Squash(chart.Chart.Title), StringComparison.Ordinal)).ToList();

        if (graphicPages == 0 || missingTitles.Count > 0)
        {
            var finding = graphicPages == 0
                ? string.Create(CultureInfo.InvariantCulture, $"The definition has {charts.Count} chart(s), but no body page draws chart graphics.")
                : "Chart titles are missing from the pages: " + PdfCheckList.List(missingTitles.Select(chart => PdfCheckList.Quote(chart.Chart.Title)), MaxListed) + ".";

            checks.Fail("Charts", finding, "Check the chart blocks' labels and series with preview_pdf; a chart with no numeric values is not drawn.");

            return;
        }

        checks.Pass("Charts", string.Create(CultureInfo.InvariantCulture, $"The {charts.Count} chart(s) are drawn as graphics on the body pages."));
    }

    private static void CheckTextWidth(PdfCheckList checks, PdfDocumentDefinition definition, List<PdfPageInspection> bodyPages, PdfCompositionOptions options)
    {
        var sections = definition.Sections ?? [];
        var geometries = sections.Select(section => ResolveGeometry(section?.PageSetup, definition, options)).ToList();

        if (geometries.Count == 0)
        {
            geometries.Add(ResolveGeometry(null, definition, options));
        }

        var hasTables = sections.SelectMany(section => section?.Blocks ?? []).Any(block => block is not null && PdfMigraDocBuilder.NormalizeType(block) == PdfBlockTypes.Table);
        var findings = new List<string>();
        var flagged = new List<int>();

        foreach (var page in bodyPages)
        {
            var (width, height) = PdfLayoutChecks.DisplaySize(page);
            var matching = geometries
                .Where(geometry => Math.Abs(geometry.Width - width) <= SizeTolerance && Math.Abs(geometry.Height - height) <= SizeTolerance)
                .ToList();

            if (matching.Count == 0 || page.Page.Rotation.Value % 360 != 0)
            {
                continue;
            }

            // The most generous right margin among the sections this page could belong to.
            var limit = page.Visible.Left + matching.Max(geometry => geometry.Width - geometry.Right) + 2;
            var beyond = page.VisibleLetters
                .Select(PdfPageInspection.Box)
                .Concat(page.Paths.Where(path => path.IsVisible && !PdfOverflowChecks.IsBackground(path.Box, page)).Select(path => path.Box))
                .Where(box => box.Right > limit)
                .ToList();

            if (beyond.Count == 0)
            {
                continue;
            }

            flagged.Add(page.Number);
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"page {page.Number} by up to {PdfCheckList.Millimetres(beyond.Max(box => box.Right) - limit + 2)}"));
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Text width", hasTables
                ? "All content, tables included, stays within the text width of its page setup."
                : "All content stays within the text width of its page setup.");

            return;
        }

        var finding = "Content runs past the right margin of the page setup: " + PdfCheckList.List(findings, MaxListed) + ".";

        if (hasTables)
        {
            finding += " A table wider than the text is the usual cause.";
        }

        checks.Warn(
            "Text width",
            finding,
            "Give the table fewer or narrower columns, a smaller font, or put it in a landscape section.");
    }

    private static List<(string Text, int Level)> Headings(PdfDocumentDefinition definition)
    {
        var headings = new List<(string Text, int Level)>();

        foreach (var block in (definition.Sections ?? []).SelectMany(section => section?.Blocks ?? []))
        {
            if (block is null || string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            var type = PdfMigraDocBuilder.NormalizeType(block);

            if (type == PdfBlockTypes.Heading)
            {
                headings.Add((RichTextParser.ToPlainText(RichTextParser.Parse(block.Text)), Math.Clamp(block.Level ?? 1, 1, 4)));
            }
            else if (type == PdfBlockTypes.Markdown)
            {
                foreach (var rich in RichTextParser.Parse(block.Text))
                {
                    if (rich.Kind == RichTextBlockKind.Heading)
                    {
                        headings.Add((string.Concat(rich.Spans.Select(span => span.Text)), Math.Clamp(rich.Level, 1, 4)));
                    }
                }
            }
        }

        return [.. headings.Where(heading => !string.IsNullOrWhiteSpace(heading.Text))];
    }

    private static bool IsMatch(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string BandText(PdfPageInspection page, bool top)
    {
        var band = Math.Max(page.Visible.Height * 0.15, 36);
        var words = PdfPageText.GetWords(page.Page)
            .Where(word =>
            {
                var box = PdfBox.From(word.BoundingBox);

                return top
                    ? box.Bottom >= page.Visible.Top - band
                    : box.Top <= page.Visible.Bottom + band;
            })
            .OrderByDescending(word => Math.Round(word.BoundingBox.Bottom))
            .ThenBy(word => word.BoundingBox.Left)
            .Select(word => word.Text);

        return string.Join(' ', words);
    }

    private static string BuildPattern(string template, PdfDocumentDefinition definition, int? pageNumber)
    {
        var builder = new StringBuilder();
        var index = 0;

        foreach (Match match in Token().Matches(template))
        {
            builder.Append(Regex.Escape(Squash(template[index..match.Index])));

            var token = match.Groups[1].Value.ToLowerInvariant();

            builder.Append(token switch
            {
                "page" when pageNumber is not null => pageNumber.Value.ToString(CultureInfo.InvariantCulture),
                "page" or "pages" => "[0-9a-z]{1,8}",
                "title" => Regex.Escape(Squash(definition.Title ?? string.Empty)),
                "author" => Regex.Escape(Squash(definition.Author ?? string.Empty)),
                _ => ".{0,40}?",
            });

            index = match.Index + match.Length;
        }

        builder.Append(Regex.Escape(Squash(template[index..])));

        return builder.ToString();
    }

    private static string Squash(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character) && character is not ('*' or '_' or '`'))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\{(page|pages|title|author|date)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
