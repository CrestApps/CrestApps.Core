using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigParsingOptions = UglyToad.PdfPig.ParsingOptions;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the integrity of a PDF file: its header and end, its revisions, whether strict and lenient readers
/// open it, its page tree, page boxes, content streams, resources, fonts, references, encryption and active
/// content.
/// </summary>
internal static class PdfIntegrityChecks
{
    private const int MaxListed = 6;

    /// <summary>
    /// Runs every integrity check.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="password">The password the document was opened with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The checks.</returns>
    public static PdfCheckList Run(PdfInspectedDocument document, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var checks = new PdfCheckList();
        var bytes = document.Bytes;

        CheckHeader(checks, bytes, document.Objects);
        CheckEnd(checks, bytes);
        CheckRevisions(checks, bytes);
        CheckReaders(checks, document, password);
        CheckPageCount(checks, document);

        if (document.Objects is not null)
        {
            CheckPageBoxes(checks, document.Objects);

            cancellationToken.ThrowIfCancellationRequested();

            var pages = Enumerable.Range(1, document.Objects.PageCount).ToList();
            var scan = PdfContentScanner.Scan(document.Objects, pages, cancellationToken);

            CheckContentStreams(checks, scan);
            CheckResources(checks, scan);
            CheckFonts(checks, document.Objects, pages, scan);
            CheckReferences(checks, document.Objects);
            CheckActiveContent(checks, document.Objects);
        }
        else
        {
            checks.Info("Objects, content streams and fonts", "Not checked: the file's objects could not be read (see above).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        CheckPageReading(checks, document.Content, cancellationToken);
        CheckEncryption(checks, document);

        return checks;
    }

    /// <summary>
    /// Counts how often a marker occurs in a file.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="marker">The marker, in ASCII.</param>
    /// <returns>The count.</returns>
    public static int Count(byte[] bytes, string marker)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var needle = Encoding.ASCII.GetBytes(marker);
        var span = bytes.AsSpan();
        var count = 0;

        while (true)
        {
            var index = span.IndexOf(needle);

            if (index < 0)
            {
                return count;
            }

            count++;
            span = span[(index + needle.Length)..];
        }
    }

    /// <summary>
    /// Returns whether the file is linearized ("fast web view").
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns><see langword="true"/> when the first object is a linearization dictionary.</returns>
    public static bool IsLinearized(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));

        return head.Contains("/Linearized", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the version a file's header declares.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="offset">Where the header starts, or -1 when there is none.</param>
    /// <returns>The version, for example <c>1.7</c>, or <see langword="null"/>.</returns>
    public static string ReadHeaderVersion(byte[] bytes, out int offset)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1024));

        offset = head.IndexOf("%PDF-", StringComparison.Ordinal);

        if (offset < 0 || offset + 8 > head.Length)
        {
            return null;
        }

        var version = head.Substring(offset + 5, 3);

        return char.IsAsciiDigit(version[0]) && version[1] == '.' && char.IsAsciiDigit(version[2])
            ? version
            : null;
    }

    private static void CheckHeader(PdfCheckList checks, byte[] bytes, PdfDocument objects)
    {
        var version = ReadHeaderVersion(bytes, out var offset);

        if (offset < 0)
        {
            checks.Fail("File header", "The file does not start with a %PDF- header, so it is not a PDF or it is badly damaged.");

            return;
        }

        if (version is null)
        {
            checks.Fail("File header", "The %PDF- header carries no readable version number.");

            return;
        }

        var catalogVersion = PdfObjectReader.GetName(objects?.Internals.Catalog, "/Version")?.TrimStart('/');
        var effective = catalogVersion is not null && string.CompareOrdinal(catalogVersion, version) > 0
            ? $"PDF {version}, raised to {catalogVersion} by the document catalog"
            : $"PDF {version}";

        if (offset > 0)
        {
            checks.Warn("File header", string.Create(CultureInfo.InvariantCulture, $"{effective}, but {offset} byte(s) of other data come before the header; strict readers reject such a file."), "Re-save the file with a PDF editor, or rebuild it.");

            return;
        }

        checks.Pass("File header", effective + ".");
    }

    private static void CheckEnd(PdfCheckList checks, byte[] bytes)
    {
        var tailLength = Math.Min(bytes.Length, 2048);
        var tail = Encoding.Latin1.GetString(bytes, bytes.Length - tailLength, tailLength);

        if (!tail.Contains("%%EOF", StringComparison.Ordinal))
        {
            checks.Fail("End of file", "The file does not end with %%EOF: it is probably truncated (an incomplete download or copy).", "Obtain the file again from its source.");

            return;
        }

        if (!tail.Contains("startxref", StringComparison.Ordinal))
        {
            checks.Warn("End of file", "The file ends with %%EOF but has no startxref pointer before it, so readers must rebuild its cross-reference table.");

            return;
        }

        checks.Pass("End of file", "The file ends with a cross-reference pointer and %%EOF.");
    }

    private static void CheckRevisions(PdfCheckList checks, byte[] bytes)
    {
        var markers = Count(bytes, "%%EOF");
        var linearized = IsLinearized(bytes);

        // A linearized file carries a second end marker after its first-page section.
        var expected = linearized
            ? 2
            : 1;
        var updates = Math.Max(0, markers - expected);

        if (updates > 0)
        {
            var finding = string.Create(CultureInfo.InvariantCulture, $"The file was saved {updates} more time(s) by appending changes (incremental updates); the earlier revisions are still inside the file and can be recovered.");

            if (linearized)
            {
                finding += " Its linearization no longer matches the file, so fast web view will not work.";
            }

            checks.Info(
                "Incremental updates",
                finding,
                "If earlier content must not be recoverable, save a fresh copy (for example with sanitize_pdf or optimize_pdf). If the file is signed, verify_pdf_signature tells whether the changes came after signing.");
        }
        else
        {
            checks.Pass("Incremental updates", "The file was written in one piece (no incremental updates).");
        }

        checks.Info(
            "Linearization",
            linearized
                ? "The file is linearized (fast web view): its first page can be shown before the rest has downloaded."
                : "The file is not linearized (no fast web view); this only matters for large files viewed over the web.");
    }

    private static void CheckReaders(PdfCheckList checks, PdfInspectedDocument document, string password)
    {
        if (document.Objects is null)
        {
            var finding = "PDFsharp, a strict reader, cannot open the file: " + document.ObjectsError;

            if (document.Content is not null)
            {
                finding += " A repairing reader (PdfPig) does open it, so viewers that repair files will probably show it.";
            }

            checks.Fail(
                "Object structure",
                finding,
                "Open and re-save the file in a PDF editor, or rebuild it from its source; many editing tools of this agent need the file to open strictly.");
        }
        else if (document.ReaderProblems.Count > 0)
        {
            checks.Warn("Object structure", "The file opens, but the reader repaired problems: " + PdfCheckList.List(document.ReaderProblems.Distinct(StringComparer.Ordinal), MaxListed) + ".");
        }
        else
        {
            checks.Pass("Object structure", string.Create(CultureInfo.InvariantCulture, $"A strict reader opens the file and reads its {document.Objects.Internals.GetAllObjects().Length:N0} objects."));
        }

        if (document.Content is null)
        {
            checks.Fail("Content reader", "PdfPig, a repairing reader, cannot open the file: " + document.ContentError);

            return;
        }

        try
        {
            using var strict = PigDocument.Open(document.Bytes, new PigParsingOptions { UseLenientParsing = false, Password = password });

            _ = strict.NumberOfPages;

            checks.Pass("Content reader", "The file opens without repairs in both a strict and a lenient parser.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            checks.Warn("Content reader", "The file opens, but only when the reader repairs it; a strict parser reports: " + ex.Message, "Re-save the file with a PDF editor so every viewer reads it the same way.");
        }
    }

    private static void CheckPageCount(PdfCheckList checks, PdfInspectedDocument document)
    {
        var counts = new List<string>();
        var values = new HashSet<int>();

        if (document.Content is not null)
        {
            counts.Add(string.Create(CultureInfo.InvariantCulture, $"the content reader finds {document.Content.NumberOfPages}"));
            values.Add(document.Content.NumberOfPages);
        }

        if (document.Objects is not null)
        {
            counts.Add(string.Create(CultureInfo.InvariantCulture, $"the object reader finds {document.Objects.PageCount}"));
            values.Add(document.Objects.PageCount);

            var declared = PdfObjectReader.GetNumber(PdfObjectReader.GetDictionary(document.Objects.Internals.Catalog, "/Pages"), "/Count");

            if (declared is not null)
            {
                counts.Add(string.Create(CultureInfo.InvariantCulture, $"the page tree declares {(int)declared.Value}"));
                values.Add((int)declared.Value);
            }
            else
            {
                checks.Fail("Page tree", "The document catalog has no page tree with a page count.");

                return;
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        if (values.Count > 1)
        {
            checks.Fail("Page count", "The page counts disagree: " + string.Join(", ", counts) + ". Viewers may show a different number of pages.", "Rebuild the page tree by re-saving the file in a PDF editor.");

            return;
        }

        var pageCount = values.First();

        if (pageCount == 0)
        {
            checks.Fail("Page count", "The document has no pages.");

            return;
        }

        checks.Pass("Page count", string.Create(CultureInfo.InvariantCulture, $"{pageCount} page(s), agreed by every reader."));
    }

    private static void CheckPageBoxes(PdfCheckList checks, PdfDocument objects)
    {
        var missing = new List<int>();
        var degenerate = new List<int>();
        var huge = new List<int>();
        var cropOutside = new List<int>();
        var cropInvalid = new List<int>();
        var badRotation = new List<int>();

        for (var index = 0; index < objects.PageCount; index++)
        {
            var page = objects.Pages[index];
            var number = index + 1;
            var media = PdfObjectReader.AsNumbers(PdfObjectReader.GetInherited(page, "/MediaBox"));

            if (media is not { Count: 4 })
            {
                missing.Add(number);

                continue;
            }

            var mediaBox = Normalize(media);

            if (mediaBox.Width < 3 || mediaBox.Height < 3)
            {
                degenerate.Add(number);
            }
            else if (mediaBox.Width > 14_400 || mediaBox.Height > 14_400)
            {
                huge.Add(number);
            }

            var cropItem = PdfObjectReader.GetInherited(page, "/CropBox");

            if (cropItem is not null)
            {
                var crop = PdfObjectReader.AsNumbers(cropItem);

                if (crop is not { Count: 4 } || Normalize(crop).Width < 1 || Normalize(crop).Height < 1)
                {
                    cropInvalid.Add(number);
                }
                else
                {
                    var cropBox = Normalize(crop);

                    if (cropBox.Left < mediaBox.Left - 1 || cropBox.Bottom < mediaBox.Bottom - 1 || cropBox.Right > mediaBox.Right + 1 || cropBox.Top > mediaBox.Top + 1)
                    {
                        cropOutside.Add(number);
                    }
                }
            }

            var rotation = PdfObjectReader.AsNumber(PdfObjectReader.GetInherited(page, "/Rotate"));

            if (rotation is not null && Math.Abs(rotation.Value % 90) > 0.001)
            {
                badRotation.Add(number);
            }
        }

        var problems = 0;

        if (missing.Count > 0)
        {
            problems++;
            checks.Fail("Page boxes", $"{PdfCheckList.Pages(missing, "has", "have")} no valid MediaBox, so their size is undefined; viewers fall back to a default size.", "Re-save the file in a PDF editor, or rebuild the pages.");
        }

        if (degenerate.Count > 0)
        {
            problems++;
            checks.Fail("Page boxes", $"{PdfCheckList.Pages(degenerate, "has", "have")} a MediaBox less than 3 points wide or high, so nothing on them can be seen.");
        }

        if (huge.Count > 0)
        {
            problems++;
            checks.Warn("Page boxes", $"{PdfCheckList.Pages(huge, "is", "are")} larger than 200 inches (14,400 points), beyond what many viewers and printers accept.");
        }

        if (cropInvalid.Count > 0)
        {
            problems++;
            checks.Fail("Page boxes", $"{PdfCheckList.Pages(cropInvalid, "has", "have")} a malformed or empty CropBox, so viewers may show nothing.", "Crop the pages again (edit_pdf_pages, operation 'crop') or remove the CropBox.");
        }

        if (cropOutside.Count > 0)
        {
            problems++;
            checks.Warn("Page boxes", $"{PdfCheckList.Pages(cropOutside, "has", "have")} a CropBox that reaches beyond the MediaBox; viewers clip it to the MediaBox.");
        }

        if (badRotation.Count > 0)
        {
            problems++;
            checks.Fail("Page boxes", $"{PdfCheckList.Pages(badRotation, "is", "are")} rotated by an angle that is not a multiple of 90 degrees, which viewers ignore or reject.", "Rotate the pages again with edit_pdf_pages (operation 'rotate').");
        }

        if (problems == 0)
        {
            checks.Pass("Page boxes", string.Create(CultureInfo.InvariantCulture, $"All {objects.PageCount} page(s) have a valid MediaBox, and every CropBox lies within it."));
        }
    }

    private static void CheckContentStreams(PdfCheckList checks, PdfContentScanResult scan)
    {
        if (scan.Errors.Count > 0)
        {
            checks.Fail(
                "Content streams",
                $"The drawing instructions of {PdfCheckList.Pages(scan.Errors.Select(error => error.Page))} cannot be fully read: " +
                    PdfCheckList.List(scan.Errors.Select(Describe), MaxListed) + ". Viewers stop drawing those pages at the damage, so content may be missing.",
                "Rebuild the damaged pages from the source document, or re-save the file in a PDF editor and check the result with preview_pdf.");
        }
        else
        {
            checks.Pass("Content streams", string.Create(CultureInfo.InvariantCulture, $"The drawing instructions of all {scan.ScannedPages.Count} page(s) parse without syntax errors."));
        }

        if (scan.Warnings.Count > 0)
        {
            checks.Warn(
                "Content stream details",
                $"{PdfCheckList.Pages(scan.Warnings.Select(warning => warning.Page), "has", "have")} irregular drawing instructions that viewers usually tolerate: " +
                    PdfCheckList.List(scan.Warnings.Select(Describe), MaxListed) + ".");
        }
    }

    private static void CheckResources(PdfCheckList checks, PdfContentScanResult scan)
    {
        if (scan.MissingResources.Count == 0)
        {
            checks.Pass("Resources", "Every font, image, form and graphics state the content names is defined in its resources.");

            return;
        }

        var descriptions = scan.MissingResources
            .Select(missing => Locate(missing.Page, missing.Where) + $" uses {missing.Kind} {missing.Name}")
            .Distinct(StringComparer.Ordinal);

        checks.Fail(
            "Resources",
            "The content names resources that are not defined: " + PdfCheckList.List(descriptions, MaxListed) + ". Text in a missing font, or a missing image, is not drawn.",
            "Rebuild the affected pages from the source document; the missing resources cannot be recovered from this file.");
    }

    private static void CheckFonts(PdfCheckList checks, PdfDocument objects, List<int> pages, PdfContentScanResult scan)
    {
        var fonts = PdfFontInventory.Collect(objects, pages);
        var unmapped = fonts
            .Where(font => !font.HasUnicodeMapping && scan.TextFontPages.ContainsKey(font.Dictionary))
            .ToList();

        if (unmapped.Count == 0)
        {
            checks.Pass(
                "Unicode mapping",
                fonts.Count == 0
                    ? "The document uses no fonts."
                    : "Every font that draws text maps its glyphs back to Unicode, through a ToUnicode map or a standard encoding.");

            return;
        }

        checks.Warn(
            "Unicode mapping",
            "Text is drawn in fonts with no ToUnicode map and no standard encoding, so copying, searching and screen readers may get garbage: " +
                PdfCheckList.List(
                    unmapped
                        .GroupBy(font => font.Describe(), StringComparer.Ordinal)
                        .Select(group => group.Key + " on " + PdfCheckList.Pages(group.SelectMany(font => scan.TextFontPages[font.Dictionary]))),
                    MaxListed) + ".",
            "Check the text with extract_pdf_text; if it is garbled, recognise it with ocr_pdf or rebuild the file from its source.");
    }

    private static void CheckReferences(PdfCheckList checks, PdfDocument objects)
    {
        var broken = PdfObjectReader.FindBrokenReferences(objects);

        if (broken.Count == 0)
        {
            checks.Pass("References", "Every object reference points at an object that exists.");

            return;
        }

        checks.Warn(
            "References",
            string.Create(CultureInfo.InvariantCulture, $"{broken.Count} or more reference(s) point at objects that do not exist, which readers treat as empty: ") +
                PdfCheckList.List(broken, MaxListed) + ".",
            "Re-save the file in a PDF editor; if something is missing from the pages, rebuild it from the source.");
    }

    private static void CheckActiveContent(PdfCheckList checks, PdfDocument objects)
    {
        var active = PdfActiveContent.Scan(objects);
        var findings = new List<string>();

        if (active.DocumentScripts.Count > 0)
        {
            findings.Add("document-level scripts " + PdfCheckList.List(active.DocumentScripts.Select(name => PdfCheckList.Quote(name)), MaxListed));
        }

        foreach (var group in active.Actions.GroupBy(action => action.Type, StringComparer.Ordinal))
        {
            findings.Add($"{group.Key} actions on {PdfCheckList.List(group.Select(action => action.Location).Distinct(StringComparer.Ordinal), 4)}");
        }

        if (active.AdditionalActions.Count > 0)
        {
            findings.Add("event actions (run on opening, closing or using) on " + PdfCheckList.List(active.AdditionalActions.Distinct(StringComparer.Ordinal), 4));
        }

        if (findings.Count == 0)
        {
            checks.Pass("Active content", "No JavaScript, launch actions or event-triggered actions.");

            return;
        }

        var status = active.HasJavaScript || active.HasLaunch
            ? PdfCheckStatus.Warn
            : PdfCheckStatus.Info;

        checks.Add(
            status,
            "Active content",
            "The file can run actions by itself: " + string.Join("; ", findings) + ".",
            "If the file comes from an untrusted source or must be archived, remove them with sanitize_pdf.");
    }

    private static void CheckPageReading(PdfCheckList checks, PigDocument content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return;
        }

        var unreadable = new List<string>();
        var unreadablePages = new List<int>();
        var garbled = new List<string>();
        var garbledPages = new List<int>();

        for (var number = 1; number <= content.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var page = content.GetPage(number);
                var letters = page.Letters;
                var counted = 0;
                var unmapped = 0;

                foreach (var letter in letters)
                {
                    if (letter.Value is " ")
                    {
                        continue;
                    }

                    counted++;

                    if (string.IsNullOrEmpty(letter.Value) || letter.Value.Any(character => character == '�' || (char.IsControl(character) && character is not ('\t' or '\n' or '\r'))))
                    {
                        unmapped++;
                    }
                }

                if (counted >= 20 && unmapped * 10 >= counted)
                {
                    garbledPages.Add(number);
                    garbled.Add(string.Create(CultureInfo.InvariantCulture, $"page {number}: {unmapped * 100 / counted}% of {counted} glyphs"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unreadablePages.Add(number);
                unreadable.Add(string.Create(CultureInfo.InvariantCulture, $"page {number}: {ex.Message}"));
            }
        }

        if (unreadable.Count > 0)
        {
            checks.Fail("Page content", $"The content of {PdfCheckList.Pages(unreadablePages)} cannot be read: {PdfCheckList.List(unreadable, 4)}.", "Rebuild the affected pages from the source document.");
        }
        else
        {
            checks.Pass("Page content", string.Create(CultureInfo.InvariantCulture, $"The text and graphics of all {content.NumberOfPages} page(s) can be read."));
        }

        if (garbled.Count > 0)
        {
            checks.Warn("Extracted text", "Many glyphs have no Unicode value, so their text extracts as garbage: " + PdfCheckList.List(garbled, MaxListed) + ".", "Recognise the text with ocr_pdf, or rebuild the file from its source.");
        }
    }

    private static void CheckEncryption(PdfCheckList checks, PdfInspectedDocument document)
    {
        var encrypted = document.Content?.IsEncrypted ??
            Encoding.Latin1.GetString(document.Bytes, Math.Max(0, document.Bytes.Length - 4096), Math.Min(document.Bytes.Length, 4096)).Contains("/Encrypt", StringComparison.Ordinal);

        checks.Info(
            "Encryption",
            encrypted
                ? "The file is encrypted; it was opened with the password given or with its empty open password. Its permissions may restrict printing, copying or changes."
                : "The file is not encrypted.");
    }

    private static string Describe(PdfContentProblem problem)
    {
        return Locate(problem.Page, problem.Where) + ": " + problem.Message;
    }

    private static string Locate(int page, string where)
    {
        return where == "page"
            ? string.Create(CultureInfo.InvariantCulture, $"page {page}")
            : string.Create(CultureInfo.InvariantCulture, $"page {page} ({where})");
    }

    private static PdfBox Normalize(List<double> box)
    {
        return new PdfBox(Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Max(box[0], box[2]), Math.Max(box[1], box[3]));
    }
}
