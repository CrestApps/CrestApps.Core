using System.Globalization;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the accessibility requirements of a PDF that can be verified from the file: tagging, language,
/// title, alternative text, extractable text, fonts, bookmarks, form field and link descriptions, tab order,
/// table headers and headings.
/// </summary>
internal static partial class PdfAccessibilityChecks
{
    private const int MaxListed = 6;
    private const int BookmarkPageThreshold = 20;

    private const string RebuildFix = "An untagged PDF cannot be given a full structure tree afterwards. Rebuild it with create_pdf and add_pdf_content, or re-export it from its source application with tagging (\"accessible PDF\") turned on.";

    /// <summary>
    /// Runs the accessibility checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="facts">The document's facts.</param>
    public static void Run(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(facts);

        var tagged = facts.IsMarked && facts.HasStructureTree;

        CheckTagging(checks, facts);
        CheckLanguage(checks, facts);
        CheckTitle(checks, facts);
        CheckFigures(checks, facts, tagged);
        CheckExtractableText(checks, facts);
        CheckFonts(checks, facts);
        CheckBookmarks(checks, facts);
        CheckFormFields(checks, facts);
        CheckLinks(checks, facts);
        CheckTabOrder(checks, facts);
        CheckTables(checks, facts, tagged);
        CheckHeadings(checks, facts, tagged);
    }

    /// <summary>
    /// Returns whether a language tag has the shape of a BCP 47 tag, such as <c>en</c>, <c>en-US</c> or <c>zh-Hant-TW</c>.
    /// </summary>
    /// <param name="language">The tag.</param>
    /// <returns><see langword="true"/> for a well-formed tag.</returns>
    public static bool IsLanguageTag(string language)
    {
        return !string.IsNullOrWhiteSpace(language) && LanguageTag().IsMatch(language.Trim());
    }

    private static void CheckTagging(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (facts.HasStructureTree && facts.IsMarked)
        {
            checks.Pass("Tagged PDF", string.Create(CultureInfo.InvariantCulture, $"The document is tagged: its structure tree has {facts.Elements.Count} element(s)."));

            if (facts.IsSuspect)
            {
                checks.Warn("Tag reliability", "The producer marked its tags as suspect (/MarkInfo /Suspects true): they may not match the content.", "Review the tags in a tag editor, or rebuild the document.");
            }

            return;
        }

        if (facts.HasStructureTree)
        {
            checks.Fail("Tagged PDF", "The document has a structure tree, but /MarkInfo does not declare it tagged, so assistive technology may ignore the tags.", "Run tag_pdf_accessibility, which marks the document as tagged.");

            return;
        }

        checks.Fail("Tagged PDF", "The document is not tagged: it has no structure tree, so screen readers get no headings, lists, tables or reading order.", RebuildFix);
    }

    private static void CheckLanguage(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (string.IsNullOrWhiteSpace(facts.Language))
        {
            checks.Fail("Document language", "No document language (/Lang) is set, so screen readers guess which voice and pronunciation to use.", "Run tag_pdf_accessibility with 'language', for example \"en-US\" (detect_pdf_language finds it).");

            return;
        }

        if (!IsLanguageTag(facts.Language))
        {
            checks.Warn("Document language", $"The document language {PdfCheckList.Quote(facts.Language)} is not a well-formed language tag.", "Run tag_pdf_accessibility with a BCP 47 'language', for example \"en-US\" or \"fr\".");

            return;
        }

        checks.Pass("Document language", $"The document language is {facts.Language}.");
    }

    private static void CheckTitle(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        var title = facts.Title ?? facts.Xmp?.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            checks.Fail("Document title", "The document has no title, so a screen reader announces its file name instead.", "Run tag_pdf_accessibility with 'title' (or let it take the first heading of page 1).");
        }
        else
        {
            checks.Pass("Document title", $"The title is {PdfCheckList.Quote(title, 80)}.");
        }

        if (facts.DisplayDocTitle == true)
        {
            checks.Pass("Title in the window", "Viewers are asked to show the title, not the file name (DisplayDocTitle).");
        }
        else
        {
            checks.Fail("Title in the window", "Viewers show the file name in the window title because /ViewerPreferences /DisplayDocTitle is not true.", "Run tag_pdf_accessibility, which turns it on.");
        }
    }

    private static void CheckFigures(PdfCheckList checks, PdfAccessibilityFacts facts, bool tagged)
    {
        var figures = facts.Figures;

        if (!tagged)
        {
            if (facts.PagesWithImages.Count > 0)
            {
                checks.Fail("Alternative text", $"Pictures on {PdfCheckList.Pages(facts.PagesWithImages)} have no alternative text: the document is untagged, so there is nowhere to put it.", RebuildFix);
            }
            else
            {
                checks.Pass("Alternative text", "The document shows no pictures that would need alternative text.");
            }

            return;
        }

        if (figures.Count == 0)
        {
            if (facts.PagesWithImages.Count > 0)
            {
                checks.Warn("Alternative text", $"Pictures are shown on {PdfCheckList.Pages(facts.PagesWithImages)}, but the structure has no Figure elements: they are either decorative artifacts or untagged.", "Check them in a tag editor; meaningful pictures need a Figure tag with alternative text.");
            }
            else
            {
                checks.Pass("Alternative text", "The document has no figures.");
            }

            return;
        }

        var missing = figures
            .Select((figure, index) => (Figure: figure, Number: index + 1))
            .Where(entry => string.IsNullOrWhiteSpace(entry.Figure.Alt) && string.IsNullOrWhiteSpace(entry.Figure.ActualText))
            .ToList();

        if (missing.Count == 0)
        {
            checks.Pass("Alternative text", string.Create(CultureInfo.InvariantCulture, $"All {figures.Count} figure(s) have alternative text."));

            return;
        }

        var described = missing.Select(entry =>
        {
            var page = facts.PageOf(entry.Figure);

            return page is null
                ? string.Create(CultureInfo.InvariantCulture, $"figure {entry.Number}")
                : string.Create(CultureInfo.InvariantCulture, $"figure {entry.Number} (page {page})");
        });

        checks.Fail(
            "Alternative text",
            string.Create(CultureInfo.InvariantCulture, $"{missing.Count} of {figures.Count} figure(s) have no alternative text: ") + PdfCheckList.List(described, MaxListed) + ".",
            "Describe each picture (analyze_pdf_images helps), then run tag_pdf_accessibility with 'figure_alt_texts', one text per figure without one, in document order.");
    }

    private static void CheckExtractableText(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (facts.ImageOnlyPages.Count == 0)
        {
            checks.Pass("Extractable text", "Every page with pictures also carries real text; no page is only a scanned image.");

            return;
        }

        checks.Fail(
            "Extractable text",
            $"{PdfCheckList.Pages(facts.ImageOnlyPages, "is", "are")} pictures of text with no text layer, so screen readers, search and copy find nothing there.",
            "Run ocr_pdf to add a searchable text layer.");
    }

    private static void CheckFonts(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        var unmapped = facts.Fonts.Where(font => !font.HasUnicodeMapping).ToList();
        var notEmbedded = facts.Fonts.Where(font => !font.IsEmbedded).ToList();

        if (unmapped.Count > 0)
        {
            checks.Fail(
                "Fonts",
                "Fonts do not map their glyphs to Unicode, so screen readers may read their text as nonsense: " +
                    PdfCheckList.List(unmapped.Select(font => font.Describe()).Distinct(StringComparer.Ordinal), MaxListed) + ".",
                "Re-export the document from its source with embedded fonts, or recognise the text with ocr_pdf.");

            return;
        }

        if (notEmbedded.Count > 0)
        {
            checks.Warn(
                "Fonts",
                "Fonts are not embedded (PDF/UA requires embedding): " + PdfCheckList.List(notEmbedded.Select(font => font.Describe()).Distinct(StringComparer.Ordinal), MaxListed) + ".",
                "Re-export the document with all fonts embedded.");

            return;
        }

        checks.Pass(
            "Fonts",
            facts.Fonts.Count == 0
                ? "The document uses no fonts."
                : string.Create(CultureInfo.InvariantCulture, $"All {facts.Fonts.Select(font => font.Describe()).Distinct(StringComparer.Ordinal).Count()} font(s) are embedded and map to Unicode."));
    }

    private static void CheckBookmarks(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (facts.BookmarkCount > 0)
        {
            checks.Pass("Bookmarks", string.Create(CultureInfo.InvariantCulture, $"The document has {facts.BookmarkCount} bookmark(s) to navigate by."));

            return;
        }

        if (facts.PageCount > BookmarkPageThreshold)
        {
            checks.Fail("Bookmarks", string.Create(CultureInfo.InvariantCulture, $"The document has {facts.PageCount} pages but no bookmarks, so it can only be navigated page by page."), "Add bookmarks for the headings with add_pdf_bookmarks.");

            return;
        }

        checks.Info("Bookmarks", string.Create(CultureInfo.InvariantCulture, $"No bookmarks; they are expected from {BookmarkPageThreshold + 1} pages on."));
    }

    private static void CheckFormFields(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (facts.Fields.Count == 0)
        {
            checks.Info("Form field descriptions", "The document has no form fields.");

            return;
        }

        var missing = facts.Fields.Where(field => string.IsNullOrWhiteSpace(field.Tooltip)).ToList();

        if (missing.Count == 0)
        {
            checks.Pass("Form field descriptions", string.Create(CultureInfo.InvariantCulture, $"All {facts.Fields.Count} form field(s) have a tooltip (/TU) screen readers announce."));

            return;
        }

        checks.Fail(
            "Form field descriptions",
            string.Create(CultureInfo.InvariantCulture, $"{missing.Count} of {facts.Fields.Count} form field(s) have no tooltip (/TU), so screen readers announce only their type: ") +
                PdfCheckList.List(missing.Select(field => PdfCheckList.Quote(field.FullName)), MaxListed) + ".",
            "Run tag_pdf_accessibility; it derives tooltips from the field names, or takes them from 'field_tooltips'.");
    }

    private static void CheckLinks(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        var links = facts.Annotations.Where(annotation => annotation.IsLink).ToList();

        if (links.Count == 0)
        {
            checks.Info("Link descriptions", "The document has no link annotations.");

            return;
        }

        var missing = links.Where(link => string.IsNullOrWhiteSpace(link.Contents)).ToList();

        if (missing.Count == 0)
        {
            checks.Pass("Link descriptions", string.Create(CultureInfo.InvariantCulture, $"All {links.Count} link(s) have a description (/Contents)."));

            return;
        }

        checks.Fail(
            "Link descriptions",
            string.Create(CultureInfo.InvariantCulture, $"{missing.Count} of {links.Count} link(s) have no description (/Contents), on {PdfCheckList.Pages(missing.Select(link => link.Page))}."),
            "Run tag_pdf_accessibility; it describes each link by its text and target.");
    }

    private static void CheckTabOrder(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (facts.Annotations.Count == 0)
        {
            checks.Info("Tab order", "The document has no annotations or form fields to tab through.");

            return;
        }

        if (facts.PagesMissingTabOrder.Count == 0)
        {
            checks.Pass("Tab order", "Every page with annotations tabs through them in structure order (/Tabs /S).");

            return;
        }

        checks.Fail(
            "Tab order",
            $"{PdfCheckList.Pages(facts.PagesMissingTabOrder, "has", "have")} links, fields or other annotations but no structure tab order (/Tabs /S), so keyboard users may tab through them out of order.",
            "Run tag_pdf_accessibility, which sets it.");
    }

    private static void CheckTables(PdfCheckList checks, PdfAccessibilityFacts facts, bool tagged)
    {
        if (!tagged)
        {
            checks.Info("Table headers", "Not checked: the document is untagged, so any tables it shows have no header cells for screen readers.");

            return;
        }

        var tables = facts.Elements.Count(element => element.StandardType == "Table");

        if (tables == 0)
        {
            checks.Info("Table headers", "The structure has no tables.");

            return;
        }

        var headers = facts.Elements.Count(element => element.StandardType == "TH");

        if (headers == 0)
        {
            checks.Fail("Table headers", string.Create(CultureInfo.InvariantCulture, $"The structure has {tables} table(s) but no header cells (TH), so screen readers cannot say which column or row a cell belongs to."), "Tag the header cells as TH in a tag editor, or rebuild the tables with create_pdf (a table block has header columns).");

            return;
        }

        checks.Pass("Table headers", string.Create(CultureInfo.InvariantCulture, $"The {tables} table(s) have {headers} header cell(s)."));
    }

    private static void CheckHeadings(PdfCheckList checks, PdfAccessibilityFacts facts, bool tagged)
    {
        if (!tagged)
        {
            checks.Fail("Headings", "The document is untagged, so it has no headings a screen reader can list or jump between.", RebuildFix);

            return;
        }

        var headings = facts.Elements.Count(element => element.IsHeading);

        if (headings == 0)
        {
            checks.Warn("Headings", "The structure has no headings (H, H1-H6), so the document cannot be navigated by heading.", "Tag the headings in a tag editor, or rebuild the document with create_pdf using heading blocks.");

            return;
        }

        checks.Pass("Headings", string.Create(CultureInfo.InvariantCulture, $"The structure has {headings} heading(s)."));
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();
}
