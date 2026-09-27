using System.Globalization;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Improves the accessibility settings of a PDF that can be improved without re-authoring its content —
/// language, title, tagged flag, tab order, form field tooltips, link descriptions and figure alternative
/// text — and saves the result as a working copy.
/// </summary>
internal sealed class TagPdfAccessibilityTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.TagPdfAccessibility;

    private const int MaxListed = 8;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "language": {
              "type": "string",
              "description": "The document language as a BCP 47 tag, for example \"en-US\", \"fr\" or \"de-CH\". Required when the PDF has no language yet; detect_pdf_language finds it."
            },
            "title": {
              "type": "string",
              "description": "The document title. By default an existing title is kept, and a PDF without one gets the first large line of page 1."
            },
            "field_tooltips": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Optional tooltips (what a screen reader announces) by form field name, as get_pdf_form_fields lists them. Fields without a tooltip that are not listed get one made from their name, for example \"first_name\" becomes \"First name\"."
            },
            "figure_alt_texts": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Alternative text for the figures that have none, in document order: the first text describes the first figure without alternative text, and so on. Only a tagged PDF has figures; check_pdf_accessibility counts them."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagPdfAccessibilityTool"/> class.
    /// </summary>
    public TagPdfAccessibilityTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Improves a PDF's accessibility settings without re-authoring its content, and saves the result as a working PDF: sets the document language and title and makes viewers show the title, marks a PDF that has a structure tree as tagged, sets the tab order on pages with links or fields, adds tooltips to form fields, describes links, and adds alternative text to figures that lack it. Use it after check_pdf_accessibility to apply the fixes it names. It cannot add a structure tree to an untagged PDF; rebuild such a document with create_pdf when full tagging is needed. Reports every change and the name of the saved working PDF.";

    /// <summary>
    /// Improves the accessibility settings of the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was changed.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var language = arguments.GetString("language")?.Trim();

        if (language is not null && !PdfAccessibilityChecks.IsLanguageTag(language))
        {
            throw new PdfToolException($"\"{language}\" is not a language tag. Pass a BCP 47 tag such as \"en-US\", \"fr\" or \"pt-BR\".");
        }

        var title = arguments.GetString("title")?.Trim();
        var tooltips = ReadTooltips(arguments);
        var altTexts = arguments.GetStrings("figure_alt_texts");
        var password = arguments.GetString("password");

        return await context.MutateAsync(
            async state =>
            {
                var target = context.FindPdf(state, arguments.Pdf());
                var bytes = await context.ReadPdfAsync(target, cancellationToken);

                using var content = PdfFiles.OpenForReading(bytes, password);
                using var document = PdfFiles.OpenForEditing(bytes, password);

                var changes = new List<string>();
                var notes = new List<string>();
                var catalog = document.Internals.Catalog;
                var originalXmp = PdfObjects.GetDictionary(catalog, "/Metadata")?.Stream?.UnfilteredValue;

                ApplyLanguage(document, language, changes);
                ApplyTitle(document, content, title, changes, notes);
                ApplyMarked(document, changes, notes);
                ApplyTabOrder(document, changes);
                ApplyTooltips(document, tooltips, changes, notes);
                ApplyLinkDescriptions(document, content, changes);
                ApplyAltTexts(document, altTexts, changes, notes);

                if (changes.Count == 0)
                {
                    return Report(target, null, changes, notes, "Nothing needed changing; no working copy was saved.");
                }

                var saved = PdfFiles.Save(document);

                if (PdfXmpPreservation.HasForeignProperties(originalXmp))
                {
                    if (PdfXmpPreservation.TryRestore(saved, originalXmp, out var restored))
                    {
                        saved = restored;
                        changes.Add("Kept the original XMP metadata properties (such as a PDF/A or PDF/UA identification) that saving would otherwise drop.");
                    }
                    else
                    {
                        notes.Add("Saving rewrote the XMP metadata, and its extra properties (such as a PDF/A or PDF/UA identification) could not be kept; run validate_pdf_compliance before relying on a standards claim.");
                    }
                }

                var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, "Improved accessibility settings", cancellationToken);

                if (target.IsComposed)
                {
                    notes.Add($"\"{target.Name}\" is a composed document, so the changes were saved as the separate file \"{working.Name}\"; later changes to \"{target.Name}\" will not carry them. Run this tool again once its content is final, and give the composed document its language and title so every render has them.");
                }

                return Report(target, working, changes, notes, null);
            },
            cancellationToken);
    }

    private static Dictionary<string, string> ReadTooltips(PdfToolArguments arguments)
    {
        var tooltips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!arguments.TryGetElement("field_tooltips", out var element))
        {
            return tooltips;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var parsed = JsonDocument.Parse(element.GetString() ?? "{}");

                return ReadTooltips(parsed.RootElement);
            }
            catch (JsonException ex)
            {
                throw new PdfToolException("'field_tooltips' must be an object of field names and tooltips, for example {\"first_name\": \"First name\"}.", ex);
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PdfToolException("'field_tooltips' must be an object of field names and tooltips, for example {\"first_name\": \"First name\"}.");
        }

        return ReadTooltips(element);
    }

    private static Dictionary<string, string> ReadTooltips(JsonElement element)
    {
        var tooltips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (element.ValueKind != JsonValueKind.Object)
        {
            return tooltips;
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();

            if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
            {
                tooltips[property.Name.Trim()] = value.Trim();
            }
        }

        return tooltips;
    }

    private static void ApplyLanguage(PdfDocument document, string language, List<string> changes)
    {
        var existing = PdfObjects.GetText(document.Internals.Catalog, "/Lang")?.Trim();

        if (language is null)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                throw new PdfToolException("This PDF has no document language, and one is needed. Pass 'language' as a BCP 47 tag, for example \"en-US\"; detect_pdf_language finds the language of the text.");
            }

            return;
        }

        if (string.Equals(existing, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        document.Internals.Catalog.Elements["/Lang"] = TextString(language);
        changes.Add(string.IsNullOrWhiteSpace(existing)
            ? $"Set the document language to {language}."
            : $"Changed the document language from {existing} to {language}.");
    }

    private static void ApplyTitle(PdfDocument document, PigDocument content, string title, List<string> changes, List<string> notes)
    {
        var existing = document.Info.Title?.Trim();

        if (!string.IsNullOrEmpty(title))
        {
            if (!string.Equals(existing, title, StringComparison.Ordinal))
            {
                document.Info.Title = title;
                changes.Add($"Set the title to {PdfCheckList.Quote(title, 120)}.");
            }
        }
        else if (string.IsNullOrEmpty(existing))
        {
            var derived = DeriveTitle(content);

            if (derived is null)
            {
                notes.Add("The PDF has no title and page 1 has no text to take one from; pass 'title'.");
            }
            else
            {
                document.Info.Title = derived;
                changes.Add($"Set the title to {PdfCheckList.Quote(derived, 120)}, the first large line of page 1 (pass 'title' to choose another).");
            }
        }

        var preferences = PdfObjects.GetDictionary(document.Internals.Catalog, "/ViewerPreferences");

        if (PdfObjects.GetBoolean(preferences, "/DisplayDocTitle") != true && !string.IsNullOrWhiteSpace(document.Info.Title))
        {
            document.ViewerPreferences.DisplayDocTitle = true;
            changes.Add("Made viewers show the title instead of the file name (DisplayDocTitle).");
        }
    }

    private static void ApplyMarked(PdfDocument document, List<string> changes, List<string> notes)
    {
        var catalog = document.Internals.Catalog;

        if (PdfObjects.GetDictionary(catalog, "/StructTreeRoot") is null)
        {
            notes.Add("This PDF is untagged (it has no structure tree), and this tool cannot build one: that takes the document's content and its reading order. If full tagging is needed, rebuild the document with create_pdf and add_pdf_content, or re-export it from its source application with tagging turned on.");

            return;
        }

        var markInfo = PdfObjects.GetDictionary(catalog, "/MarkInfo");

        if (PdfObjects.GetBoolean(markInfo, "/Marked") == true)
        {
            return;
        }

        if (markInfo is null)
        {
            markInfo = new PdfDictionary(document);
            catalog.Elements["/MarkInfo"] = markInfo;
        }

        markInfo.Elements.SetBoolean("/Marked", true);
        changes.Add("Marked the document as tagged (/MarkInfo /Marked), since it has a structure tree.");
    }

    private static void ApplyTabOrder(PdfDocument document, List<string> changes)
    {
        var pages = new List<int>();

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];
            var annotations = PdfObjects.Items(PdfObjects.GetArray(page, "/Annots"))
                .OfType<PdfDictionary>()
                .Any(annotation => !PdfObjects.IsName(annotation, "/Subtype", "/Popup"));

            if (annotations && !PdfObjects.IsName(page, "/Tabs", "/S"))
            {
                page.Elements.SetName("/Tabs", "/S");
                pages.Add(index + 1);
            }
        }

        if (pages.Count > 0)
        {
            changes.Add($"Set the tab order to follow the structure (/Tabs /S) on {PdfCheckList.Pages(pages)}.");
        }
    }

    private static void ApplyTooltips(PdfDocument document, Dictionary<string, string> tooltips, List<string> changes, List<string> notes)
    {
        var fields = PdfFormFieldList.Read(document);
        var applied = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            var partial = field.FullName.Split('.').LastOrDefault() ?? field.FullName;
            string tooltip = null;

            if (tooltips.TryGetValue(field.FullName, out var byFullName))
            {
                tooltip = byFullName;
                used.Add(field.FullName);
            }
            else if (tooltips.TryGetValue(partial, out var byPartialName))
            {
                tooltip = byPartialName;
                used.Add(partial);
            }
            else if (string.IsNullOrWhiteSpace(field.Tooltip))
            {
                tooltip = PdfFormFieldList.Humanize(field.FullName);
            }

            if (string.IsNullOrWhiteSpace(tooltip) || string.Equals(field.Tooltip, tooltip, StringComparison.Ordinal))
            {
                continue;
            }

            field.Dictionary.Elements["/TU"] = TextString(tooltip);
            applied.Add($"{PdfCheckList.Quote(field.FullName)} → {PdfCheckList.Quote(tooltip)}");
        }

        if (applied.Count > 0)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Gave {applied.Count} form field(s) a tooltip: ") + PdfCheckList.List(applied, MaxListed) + ".");
        }

        var unknown = tooltips.Keys.Where(name => !used.Contains(name)).ToList();

        if (unknown.Count > 0)
        {
            notes.Add("No form field is named " + PdfCheckList.List(unknown.Select(name => PdfCheckList.Quote(name)), MaxListed) + "; those tooltips were not used. get_pdf_form_fields lists the field names.");
        }
    }

    private static void ApplyLinkDescriptions(PdfDocument document, PigDocument content, List<string> changes)
    {
        var links = PdfAnnotationList.Read(document, Enumerable.Range(1, document.PageCount))
            .Where(entry => entry.IsLink && string.IsNullOrWhiteSpace(entry.Contents))
            .ToList();

        var pages = new List<int>();

        foreach (var link in links)
        {
            var target = link.Action switch
            {
                "URI" when !string.IsNullOrWhiteSpace(link.Uri) => "link to " + link.Uri.Trim(),
                "GoTo" or "Dest" when link.TargetPage is not null => string.Create(CultureInfo.InvariantCulture, $"go to page {link.TargetPage}"),
                "GoToR" or "GoToE" or "Launch" when !string.IsNullOrWhiteSpace(link.RemoteFile) => "open " + link.RemoteFile,
                _ => "link",
            };

            var text = link.Rect is { } rect
                ? TextUnder(content, link.Page, rect)
                : null;

            var description = string.IsNullOrWhiteSpace(text)
                ? char.ToUpperInvariant(target[0]) + target[1..]
                : $"{text} ({target})";

            link.Dictionary.Elements["/Contents"] = TextString(description);
            pages.Add(link.Page);
        }

        if (pages.Count > 0)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Described {pages.Count} link(s) by their text and target (/Contents) on {PdfCheckList.Pages(pages)}."));
        }
    }

    private static void ApplyAltTexts(PdfDocument document, List<string> altTexts, List<string> changes, List<string> notes)
    {
        var figures = PdfStructureTree.Read(document)
            .Where(element => element.StandardType == "Figure" && string.IsNullOrWhiteSpace(element.Alt) && string.IsNullOrWhiteSpace(element.ActualText))
            .ToList();

        if (altTexts.Count == 0)
        {
            if (figures.Count > 0)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{figures.Count} figure(s) still have no alternative text; describe them (analyze_pdf_images helps) and pass the descriptions as 'figure_alt_texts', in document order."));
            }

            return;
        }

        if (figures.Count == 0)
        {
            var reason = PdfObjects.GetDictionary(document.Internals.Catalog, "/StructTreeRoot") is null
                ? " (it is untagged, so it has no figures at all)."
                : ".";

            notes.Add("'figure_alt_texts' were not used: the PDF has no figures without alternative text" + reason);

            return;
        }

        var count = Math.Min(figures.Count, altTexts.Count);

        for (var index = 0; index < count; index++)
        {
            figures[index].Dictionary.Elements["/Alt"] = TextString(altTexts[index]);
        }

        changes.Add(string.Create(CultureInfo.InvariantCulture, $"Added alternative text to {count} figure(s), in document order."));

        if (altTexts.Count > figures.Count)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{altTexts.Count - figures.Count} alternative text(s) were left over: only {figures.Count} figure(s) needed one."));
        }
        else if (figures.Count > altTexts.Count)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{figures.Count - altTexts.Count} figure(s) still have no alternative text."));
        }
    }

    private static string DeriveTitle(PigDocument content)
    {
        if (content.NumberOfPages == 0)
        {
            return null;
        }

        List<TextLine> lines;

        try
        {
            lines = [.. PdfPageText.GetBlocks(content.GetPage(1)).SelectMany(block => block.TextLines)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        var sized = lines
            .Select(line => (Line: line, Size: line.Words.SelectMany(word => word.Letters).Select(letter => letter.PointSize).DefaultIfEmpty(0).Average()))
            .Where(entry => entry.Line.Text.Trim().Length >= 3)
            .ToList();

        if (sized.Count == 0)
        {
            return null;
        }

        var largest = sized.Max(entry => entry.Size);
        var text = sized.First(entry => entry.Size >= largest * 0.85).Line.Text.Trim();

        return text.Length > 200
            ? text[..200].TrimEnd()
            : text;
    }

    private static string TextUnder(PigDocument content, int pageNumber, PdfBox rect)
    {
        try
        {
            return PdfPageInspection.TextIn(content.GetPage(pageNumber), rect);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static PdfString TextString(string text)
    {
        // Text outside PDFDocEncoding's printable ASCII is written as UTF-16 so it survives any viewer.
        return text.Any(character => character > 126)
            ? new PdfString(text, PdfStringEncoding.Unicode)
            : new PdfString(text);
    }

    private static string Report(PdfSource target, PdfWorkingDocument working, List<string> changes, List<string> notes, string outcome)
    {
        var builder = new StringBuilder();

        builder.Append("Accessibility settings of ").Append(target.Describe()).AppendLine(":");
        builder.AppendLine(outcome ?? $"Saved as working PDF \"{working.Name}\" ({working.PageCount} page(s)). The original is unchanged.");

        if (changes.Count > 0)
        {
            builder.AppendLine().AppendLine("Changes:");

            foreach (var change in changes)
            {
                builder.Append("- ").AppendLine(change);
            }
        }

        if (notes.Count > 0)
        {
            builder.AppendLine().AppendLine("Not changed:");

            foreach (var note in notes)
            {
                builder.Append("- ").AppendLine(note);
            }
        }

        var checkedPdf = working is null
            ? target.Describe()
            : $"working PDF \"{working.Name}\"";

        builder.AppendLine().Append("Run check_pdf_accessibility on ").Append(checkedPdf).Append(" to see what remains.");

        return builder.ToString();
    }
}
