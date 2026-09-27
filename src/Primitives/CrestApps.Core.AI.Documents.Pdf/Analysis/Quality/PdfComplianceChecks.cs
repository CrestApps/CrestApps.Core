using System.Globalization;
using System.Text;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the most common requirements of PDF/A (parts 1 to 3) and PDF/UA-1. The checks are indicative:
/// they cover what can be read from the file's objects, not everything a conformance validator verifies.
/// </summary>
internal static class PdfComplianceChecks
{
    private const int MaxListed = 6;

    private static readonly HashSet<string> _allowedBlendModes = new(StringComparer.Ordinal)
    {
        "/Normal",
        "/Compatible",
    };

    /// <summary>
    /// Checks a file against a part and level of PDF/A.
    /// </summary>
    /// <param name="checks">The report to add to; the checks are filed under the standard's name.</param>
    /// <param name="standard">The PDF/A standard.</param>
    /// <param name="document">The document.</param>
    /// <param name="facts">The document's facts.</param>
    public static void CheckArchive(PdfCheckList checks, PdfStandard standard, PdfInspectedDocument document, PdfAccessibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(standard);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(facts);

        var objects = document.Objects;
        var catalog = objects.Internals.Catalog;
        var xmp = facts.Xmp;
        var dictionaries = PdfObjects.EnumerateDictionaries(objects);

        checks.Category = standard.Name;

        // Metadata and identification.
        if (!xmp.IsPresent)
        {
            checks.Fail("XMP metadata", "The file has no XMP metadata stream, which PDF/A requires.", "Convert the file with a PDF/A converter; a PDF/A file cannot be made by setting a flag.");
        }
        else if (xmp.ParseError is not null)
        {
            checks.Fail("XMP metadata", "The XMP metadata cannot be read: " + xmp.ParseError + ".");
        }
        else if (standard.Part == 1 && xmp.IsFiltered)
        {
            checks.Fail("XMP metadata", "The XMP metadata stream is compressed, which PDF/A-1 does not allow.");
        }
        else
        {
            checks.Pass("XMP metadata", "The file carries readable XMP metadata.");
        }

        if (xmp.PdfAPart is null)
        {
            checks.Fail("PDF/A identification", "The metadata does not claim PDF/A (no pdfaid:part), so no viewer or archive treats the file as PDF/A.", "Convert the file with a PDF/A converter.");
        }
        else if (xmp.PdfAPart != standard.Part || !string.Equals(xmp.PdfAConformance, standard.Conformance, StringComparison.OrdinalIgnoreCase))
        {
            checks.Fail("PDF/A identification", string.Create(CultureInfo.InvariantCulture, $"The metadata claims PDF/A-{xmp.PdfAPart}{xmp.PdfAConformance?.ToLowerInvariant()}, not {standard.Name}."));
        }
        else
        {
            checks.Pass("PDF/A identification", $"The metadata claims {standard.Name}.");
        }

        CheckOutputIntent(checks, catalog);
        CheckNotEncrypted(checks, document);
        CheckFontsEmbedded(checks, facts);
        CheckArchiveActions(checks, objects);
        CheckLzw(checks, dictionaries);
        CheckTransparency(checks, standard, dictionaries);
        CheckEmbeddedFiles(checks, standard, dictionaries);
        CheckDocumentId(checks, document);
        CheckMetadataConsistency(checks, objects, xmp);

        if (standard.Part == 1 && PdfObjects.Get(catalog, "/OCProperties") is not null)
        {
            checks.Fail("Optional content", "The file has layers (optional content), which PDF/A-1 does not allow.", "Flatten or remove the layers with manage_pdf_layers.");
        }

        if (standard.Conformance is "A" or "U")
        {
            var unmapped = facts.Fonts.Where(font => !font.HasUnicodeMapping).ToList();

            if (unmapped.Count > 0)
            {
                checks.Fail("Unicode text", $"Level {standard.Conformance} requires every font to map its glyphs to Unicode; these do not: " + PdfCheckList.List(unmapped.Select(font => font.Describe()).Distinct(StringComparer.Ordinal), MaxListed) + ".");
            }
            else
            {
                checks.Pass("Unicode text", "Every font maps its glyphs to Unicode.");
            }
        }

        if (standard.Conformance == "A")
        {
            CheckTagged(checks, facts, "Level A requires a tagged document");
            CheckLanguage(checks, facts);
            CheckFigures(checks, facts);
        }
    }

    /// <summary>
    /// Checks a file against PDF/UA-1.
    /// </summary>
    /// <param name="checks">The report to add to; the checks are filed under the standard's name.</param>
    /// <param name="standard">The PDF/UA standard.</param>
    /// <param name="facts">The document's facts.</param>
    public static void CheckUniversal(PdfCheckList checks, PdfStandard standard, PdfAccessibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(standard);
        ArgumentNullException.ThrowIfNull(facts);

        checks.Category = standard.Name;

        if (facts.Xmp.PdfUAPart == 1)
        {
            checks.Pass("PDF/UA identification", "The metadata claims PDF/UA-1 (pdfuaid:part 1).");
        }
        else if (facts.Xmp.PdfUAPart is { } part)
        {
            checks.Fail("PDF/UA identification", string.Create(CultureInfo.InvariantCulture, $"The metadata claims PDF/UA-{part}, not PDF/UA-1."));
        }
        else
        {
            checks.Fail("PDF/UA identification", "The metadata does not claim PDF/UA (no pdfuaid:part).", "Only claim PDF/UA once every requirement is met, with the tool that made the file accessible.");
        }

        CheckTagged(checks, facts, "PDF/UA requires a tagged document");

        if (facts.IsSuspect)
        {
            checks.Fail("Tag reliability", "/MarkInfo /Suspects is true, which PDF/UA does not allow.");
        }

        CheckLanguage(checks, facts);

        if (string.IsNullOrWhiteSpace(facts.Xmp?.Title))
        {
            checks.Fail("Title", "The XMP metadata has no dc:title, which PDF/UA requires.", "Run tag_pdf_accessibility with 'title'.");
        }
        else
        {
            checks.Pass("Title", $"The title is {PdfCheckList.Quote(facts.Xmp.Title, 80)}.");
        }

        if (facts.DisplayDocTitle == true)
        {
            checks.Pass("Title in the window", "DisplayDocTitle is true.");
        }
        else
        {
            checks.Fail("Title in the window", "/ViewerPreferences /DisplayDocTitle is not true.", "Run tag_pdf_accessibility.");
        }

        CheckFontsEmbedded(checks, facts);
        CheckFigures(checks, facts);

        var fields = facts.Fields.Where(field => string.IsNullOrWhiteSpace(field.Tooltip)).ToList();

        if (facts.Fields.Count == 0)
        {
            checks.Info("Form fields", "The document has no form fields.");
        }
        else if (fields.Count > 0)
        {
            checks.Fail("Form fields", "Form fields have no tooltip (/TU): " + PdfCheckList.List(fields.Select(field => PdfCheckList.Quote(field.FullName)), MaxListed) + ".", "Run tag_pdf_accessibility.");
        }
        else
        {
            checks.Pass("Form fields", "Every form field has a tooltip (/TU).");
        }

        if (facts.PagesMissingTabOrder.Count > 0)
        {
            checks.Fail("Tab order", $"{PdfCheckList.Pages(facts.PagesMissingTabOrder, "has", "have")} annotations but no /Tabs /S.", "Run tag_pdf_accessibility.");
        }
        else if (facts.Annotations.Count > 0)
        {
            checks.Pass("Tab order", "Every page with annotations has /Tabs /S.");
        }

        var links = facts.Annotations.Where(annotation => annotation.IsLink).ToList();
        var undescribed = links.Where(link => string.IsNullOrWhiteSpace(link.Contents)).ToList();

        if (undescribed.Count > 0)
        {
            checks.Fail("Link descriptions", string.Create(CultureInfo.InvariantCulture, $"{undescribed.Count} of {links.Count} link(s) have no /Contents description, on {PdfCheckList.Pages(undescribed.Select(link => link.Page))}."), "Run tag_pdf_accessibility.");
        }
        else if (links.Count > 0)
        {
            checks.Pass("Link descriptions", "Every link has a /Contents description.");
        }

        if (facts.Elements.Any(element => element.StandardType == "Table") && !facts.Elements.Any(element => element.StandardType == "TH"))
        {
            checks.Warn("Table headers", "The structure has tables but no header cells (TH).");
        }
    }

    private static void CheckOutputIntent(PdfCheckList checks, PdfDictionary catalog)
    {
        var intents = PdfObjects.Items(PdfObjects.GetArray(catalog, "/OutputIntents")).OfType<PdfDictionary>().ToList();
        var archival = intents.Where(intent => PdfObjects.IsName(intent, "/S", "/GTS_PDFA1")).ToList();

        if (archival.Count == 0)
        {
            checks.Fail("Output intent", "The file has no PDF/A output intent (/OutputIntents with /S /GTS_PDFA1), which fixes how its colours are to be reproduced.", "Convert the file with a PDF/A converter, which embeds an ICC colour profile.");

            return;
        }

        if (!archival.Any(intent => PdfObjects.Get(intent, "/DestOutputProfile") is PdfDictionary { Stream: not null }))
        {
            checks.Fail("Output intent", "The PDF/A output intent has no embedded ICC profile (/DestOutputProfile).");

            return;
        }

        var condition = archival.Select(intent => PdfObjects.GetText(intent, "/OutputConditionIdentifier")).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        checks.Pass("Output intent", condition is null
            ? "The file has a PDF/A output intent with an embedded ICC profile."
            : $"The file has a PDF/A output intent ({condition}) with an embedded ICC profile.");
    }

    private static void CheckNotEncrypted(PdfCheckList checks, PdfInspectedDocument document)
    {
        if (IsEncrypted(document))
        {
            checks.Fail("Encryption", "The file is encrypted, which PDF/A does not allow.", "Remove the protection with remove_pdf_security.");

            return;
        }

        checks.Pass("Encryption", "The file is not encrypted.");
    }

    private static void CheckFontsEmbedded(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        var missing = facts.Fonts.Where(font => !font.IsEmbedded).ToList();

        if (missing.Count > 0)
        {
            checks.Fail("Fonts embedded", "Every font must be embedded, the 14 standard fonts included; these are not: " + PdfCheckList.List(missing.Select(font => font.Describe()).Distinct(StringComparer.Ordinal), MaxListed) + ".", "Re-export the document with all fonts embedded.");

            return;
        }

        checks.Pass("Fonts embedded", facts.Fonts.Count == 0
            ? "The document uses no fonts."
            : string.Create(CultureInfo.InvariantCulture, $"All {facts.Fonts.Select(font => font.Describe()).Distinct(StringComparer.Ordinal).Count()} font(s) are embedded."));
    }

    private static void CheckArchiveActions(PdfCheckList checks, PdfDocument objects)
    {
        var active = PdfActiveContent.Scan(objects);
        var findings = new List<string>();

        if (active.DocumentScripts.Count > 0)
        {
            findings.Add("document-level JavaScript");
        }

        foreach (var group in active.Actions.GroupBy(action => action.Type, StringComparer.Ordinal))
        {
            findings.Add($"{group.Key} actions ({PdfCheckList.List(group.Select(action => action.Location).Distinct(StringComparer.Ordinal), 3)})");
        }

        if (active.AdditionalActions.Count > 0)
        {
            findings.Add("event actions (/AA) on " + PdfCheckList.List(active.AdditionalActions.Distinct(StringComparer.Ordinal), 3));
        }

        if (findings.Count > 0)
        {
            checks.Fail("Scripts and actions", "PDF/A does not allow JavaScript, launch, sound, movie, reset or import actions, nor event-triggered actions; found " + string.Join("; ", findings) + ".", "Remove them with sanitize_pdf.");

            return;
        }

        checks.Pass("Scripts and actions", "No JavaScript and no forbidden or event-triggered actions.");
    }

    private static void CheckLzw(PdfCheckList checks, List<PdfDictionary> dictionaries)
    {
        var count = dictionaries.Count(dictionary => dictionary.Stream is not null && Filters(dictionary).Any(filter => filter is "/LZWDecode" or "/LZW"));

        if (count > 0)
        {
            checks.Fail("LZW compression", string.Create(CultureInfo.InvariantCulture, $"{count} stream(s) use LZW compression, which PDF/A does not allow."), "Re-save the file with optimize_pdf, which recompresses streams with Flate.");

            return;
        }

        checks.Pass("LZW compression", "No stream uses LZW compression.");
    }

    private static void CheckTransparency(PdfCheckList checks, PdfStandard standard, List<PdfDictionary> dictionaries)
    {
        var findings = new List<string>();
        var softMasks = dictionaries.Count(dictionary => PdfObjects.IsName(dictionary, "/Subtype", "/Image") && PdfObjects.Get(dictionary, "/SMask") is PdfDictionary);
        var alpha = dictionaries.Count(dictionary =>
            (PdfObjects.GetNumber(dictionary, "/CA") ?? 1) < 1 || (PdfObjects.GetNumber(dictionary, "/ca") ?? 1) < 1);

        var maskedStates = dictionaries.Count(dictionary =>
            !PdfObjects.IsName(dictionary, "/Subtype", "/Image") && PdfObjects.Get(dictionary, "/SMask") is PdfDictionary);

        var blends = dictionaries.Count(dictionary => PdfObjects.GetName(dictionary, "/BM") is { } mode && !_allowedBlendModes.Contains(mode));
        var groups = dictionaries.Count(dictionary => PdfObjects.IsName(PdfObjects.GetDictionary(dictionary, "/Group"), "/S", "/Transparency"));

        if (softMasks > 0)
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"{softMasks} image(s) with a soft mask"));
        }

        if (alpha > 0)
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"{alpha} graphics state(s) with opacity below 1"));
        }

        if (maskedStates > 0)
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"{maskedStates} soft-masked graphics state(s)"));
        }

        if (blends > 0)
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"{blends} blend mode(s)"));
        }

        if (groups > 0)
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"{groups} transparency group(s)"));
        }

        if (findings.Count == 0)
        {
            checks.Pass("Transparency", "The file uses no transparency.");

            return;
        }

        if (standard.Part == 1)
        {
            checks.Fail("Transparency", "PDF/A-1 does not allow transparency; found " + string.Join(", ", findings) + ".", "Flatten the transparency with a PDF/A converter, or target PDF/A-2b instead.");

            return;
        }

        checks.Info("Transparency", $"The file uses transparency ({string.Join(", ", findings)}), which {standard.Name} allows.");
    }

    private static void CheckEmbeddedFiles(PdfCheckList checks, PdfStandard standard, List<PdfDictionary> dictionaries)
    {
        var specifications = dictionaries.Where(dictionary => PdfObjects.GetDictionary(dictionary, "/EF") is not null).ToList();

        if (specifications.Count == 0)
        {
            checks.Pass("Embedded files", "The file has no embedded files.");

            return;
        }

        var names = specifications.Select(specification => PdfCheckList.Quote(PdfObjects.GetText(specification, "/UF") ?? PdfObjects.GetText(specification, "/F") ?? "unnamed")).ToList();

        switch (standard.Part)
        {
            case 1:
                checks.Fail("Embedded files", "PDF/A-1 does not allow embedded files; found " + PdfCheckList.List(names, MaxListed) + ".", "Remove them with manage_pdf_attachments.");

                break;
            case 2:
                var notPdf = specifications
                    .Where(specification => !IsPdfFile(specification))
                    .Select(specification => PdfCheckList.Quote(PdfObjects.GetText(specification, "/UF") ?? PdfObjects.GetText(specification, "/F") ?? "unnamed"))
                    .ToList();

                if (notPdf.Count > 0)
                {
                    checks.Fail("Embedded files", "PDF/A-2 only allows embedded files that are themselves PDF/A; these are not PDF files: " + PdfCheckList.List(notPdf, MaxListed) + ".", "Remove them with manage_pdf_attachments, or target PDF/A-3.");
                }
                else
                {
                    checks.Warn("Embedded files", "PDF/A-2 only allows embedded PDF/A files; " + PdfCheckList.List(names, MaxListed) + " are PDFs, but whether they are PDF/A was not checked.", "Check each attachment with validate_pdf_compliance after extracting it with manage_pdf_attachments.");
                }

                break;
            default:
                var unrelated = specifications
                    .Where(specification => PdfObjects.GetName(specification, "/AFRelationship") is null)
                    .Select(specification => PdfCheckList.Quote(PdfObjects.GetText(specification, "/UF") ?? PdfObjects.GetText(specification, "/F") ?? "unnamed"))
                    .ToList();

                if (unrelated.Count > 0)
                {
                    checks.Fail("Embedded files", "PDF/A-3 requires every embedded file to state its relationship to the document (/AFRelationship); these do not: " + PdfCheckList.List(unrelated, MaxListed) + ".", "Re-attach them with manage_pdf_attachments.");
                }
                else
                {
                    checks.Pass("Embedded files", "Every embedded file states its relationship to the document: " + PdfCheckList.List(names, MaxListed) + ".");
                }

                break;
        }
    }

    private static void CheckDocumentId(PdfCheckList checks, PdfInspectedDocument document)
    {
        var identifier = document.Content?.Structure.Trailer.Identifier;
        var present = identifier is not null
            ? identifier.Count >= 2
            : Tail(document.Bytes).Contains("/ID", StringComparison.Ordinal);

        if (present)
        {
            checks.Pass("Document ID", "The trailer carries a document ID.");

            return;
        }

        checks.Fail("Document ID", "The file trailer has no document ID (/ID), which PDF/A requires.", "Re-save the file; most PDF writers add one.");
    }

    private static void CheckMetadataConsistency(PdfCheckList checks, PdfDocument objects, PdfXmpInfo xmp)
    {
        if (xmp.Document is null)
        {
            return;
        }

        var info = objects.Info;
        var pairs = new (string Name, string Info, string Xmp)[]
        {
            ("title", info.Title, xmp.Title),
            ("author", info.Author, xmp.Authors),
            ("subject", info.Subject, xmp.Subject),
            ("keywords", info.Keywords, xmp.Keywords),
            ("creator", info.Creator, xmp.CreatorTool),
            ("producer", info.Producer, xmp.Producer),
        };

        var differences = pairs
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Info) && !Equivalent(pair.Info, pair.Xmp))
            .Select(pair => $"{pair.Name}: {PdfCheckList.Quote(pair.Info)} in the document information, {PdfCheckList.Quote(pair.Xmp ?? string.Empty)} in the XMP metadata")
            .ToList();

        if (differences.Count > 0)
        {
            checks.Fail("Metadata consistency", "The document information and the XMP metadata must agree; they differ in " + PdfCheckList.List(differences, MaxListed) + ".", "Set the properties again with edit_pdf_metadata, which writes both.");

            return;
        }

        checks.Pass("Metadata consistency", "The document information agrees with the XMP metadata.");
    }

    private static void CheckTagged(PdfCheckList checks, PdfAccessibilityFacts facts, string requirement)
    {
        if (facts.IsMarked && facts.HasStructureTree)
        {
            checks.Pass("Tagged PDF", string.Create(CultureInfo.InvariantCulture, $"The document is tagged ({facts.Elements.Count} structure element(s))."));

            return;
        }

        checks.Fail(
            "Tagged PDF",
            facts.HasStructureTree
                ? requirement + "; the structure tree exists but /MarkInfo /Marked is not true."
                : requirement + "; this one has no structure tree.",
            facts.HasStructureTree
                ? "Run tag_pdf_accessibility."
                : "An untagged PDF cannot be given a full structure tree afterwards; rebuild it with create_pdf, or re-export it with tagging on.");
    }

    private static void CheckLanguage(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        if (PdfAccessibilityChecks.IsLanguageTag(facts.Language))
        {
            checks.Pass("Document language", $"The document language is {facts.Language}.");

            return;
        }

        checks.Fail("Document language", "The document language (/Lang) is missing or malformed.", "Run tag_pdf_accessibility with 'language'.");
    }

    private static void CheckFigures(PdfCheckList checks, PdfAccessibilityFacts facts)
    {
        var figures = facts.Figures;
        var missing = figures.Count(figure => string.IsNullOrWhiteSpace(figure.Alt) && string.IsNullOrWhiteSpace(figure.ActualText));

        if (figures.Count == 0)
        {
            checks.Info("Figures", "The structure has no figures.");

            return;
        }

        if (missing > 0)
        {
            checks.Fail("Figures", string.Create(CultureInfo.InvariantCulture, $"{missing} of {figures.Count} figure(s) have no alternative text."), "Run tag_pdf_accessibility with 'figure_alt_texts'.");

            return;
        }

        checks.Pass("Figures", string.Create(CultureInfo.InvariantCulture, $"All {figures.Count} figure(s) have alternative text."));
    }

    private static bool Equivalent(string info, string xmp)
    {
        // XMP keeps several authors as a list where the information dictionary joins them in one string,
        // so separators and spacing are not compared.
        static string Normalize(string value)
        {
            var builder = new StringBuilder();

            foreach (var character in value ?? string.Empty)
            {
                if (!char.IsWhiteSpace(character) && character is not (',' or ';'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        return string.Equals(Normalize(info), Normalize(xmp), StringComparison.Ordinal);
    }

    private static bool IsEncrypted(PdfInspectedDocument document)
    {
        return document.Content?.IsEncrypted ?? Tail(document.Bytes).Contains("/Encrypt", StringComparison.Ordinal);
    }

    private static bool IsPdfFile(PdfDictionary specification)
    {
        var files = PdfObjects.GetDictionary(specification, "/EF");
        var stream = PdfObjects.GetDictionary(files, "/UF") ?? PdfObjects.GetDictionary(files, "/F");
        var subtype = PdfObjects.GetName(stream, "/Subtype");
        var name = PdfObjects.GetText(specification, "/UF") ?? PdfObjects.GetText(specification, "/F") ?? string.Empty;

        return (subtype is not null && subtype.Contains("pdf", StringComparison.OrdinalIgnoreCase)) ||
            name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Filters(PdfDictionary stream)
    {
        var filter = PdfObjects.Get(stream, "/Filter");

        if (filter is PdfArray array)
        {
            return PdfObjects.Items(array).Select(PdfObjects.AsName).Where(name => name is not null);
        }

        var single = PdfObjects.AsName(filter);

        return single is null
            ? []
            : [single];
    }

    private static string Tail(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 4096);

        return Encoding.Latin1.GetString(bytes, bytes.Length - length, length);
    }
}
