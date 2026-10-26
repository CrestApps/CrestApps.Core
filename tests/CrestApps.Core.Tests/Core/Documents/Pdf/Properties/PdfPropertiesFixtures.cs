using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

/// <summary>
/// Builds the PDFs the document-property tool tests work on, in memory.
/// </summary>
internal static class PdfPropertiesFixtures
{
    private static readonly PdfBrush _black = new(new PdfColor(0, 0, 0));
    private static readonly PdfBrush _gray = new(new PdfColor(0x80, 0x80, 0x80));

    /// <summary>
    /// Builds a PDF with a line of text on each page.
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <param name="title">The title.</param>
    /// <param name="pdfA">Whether the file claims PDF/A.</param>
    /// <param name="customize">Changes the document before it is saved.</param>
    /// <returns>The file.</returns>
    public static byte[] TextPdf(int pages = 2, string title = "Old Title", bool pdfA = false, Action<PdfDocument> customize = null)
    {
        using var document = new PdfDocument();

        document.Info.Title = title;
        document.Info.Author = "Old Author";

        var font = PdfFontLibrary.Get("Arial");

        for (var number = 1; number <= pages; number++)
        {
            var canvas = PdfCanvas.ForPage(LetterPage(document));

            canvas.DrawText($"Page {number}: contact us at the help desk for details.", font, 12, 72, 100, _black);
            canvas.DrawText("Visit the website for the full report.", font, 12, 72, 130, _black);
            canvas.Commit();
        }

        customize?.Invoke(document);

        var metadata = PdfMetadataSync.Capture(document);

        if (pdfA)
        {
            // The file claims PDF/A-1A in its XMP metadata and carries the output intent the standard asks for.
            PdfArchiveProfile.TryApply(document);
            metadata.ClaimPdfA(1, "A");
        }

        return metadata.Save(document, TimeProvider.System.GetUtcNow());
    }

    /// <summary>
    /// Builds a three-page report with a title, two heading levels, body text, a running head and page
    /// numbers.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] HeadingsPdf()
    {
        using var document = new PdfDocument();

        var bold = PdfFontLibrary.Get("Arial", bold: true);
        var regular = PdfFontLibrary.Get("Arial");

        var sections = new[]
        {
            ("Introduction", new[] { "Background", "Scope" }),
            ("Results", new[] { "Revenue" }),
            ("Outlook", Array.Empty<string>()),
        };

        for (var index = 0; index < sections.Length; index++)
        {
            var canvas = PdfCanvas.ForPage(LetterPage(document));

            canvas.DrawText("Contoso Internal Report", regular, 8, 72, 30, _gray);
            canvas.DrawText($"Page {index + 1}", regular, 8, 290, 770, _gray);

            var y = 80d;

            if (index == 0)
            {
                canvas.DrawText("Annual Review", bold, 24, 72, y, _black);
                y += 50;
            }

            var (name, children) = sections[index];

            canvas.DrawText(name, bold, 18, 72, y, _black);
            y += 30;
            y = DrawBody(canvas, regular, y);

            foreach (var child in children)
            {
                canvas.DrawText(child, bold, 14, 72, y, _black);
                y += 24;
                y = DrawBody(canvas, regular, y);
            }

            canvas.Commit();
        }

        return Save(document);
    }

    /// <summary>
    /// Builds a one-page PDF with two layers, "Notes" (shown) and "Draft" (hidden), each drawing a line of text.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] LayersPdf()
    {
        using var document = new PdfDocument();

        var page = LetterPage(document);
        var canvas = PdfCanvas.ForPage(page);

        canvas.DrawText("Base content", PdfFontLibrary.Get("Arial"), 12, 72, 100, _black);
        canvas.Commit();

        var notes = CreateGroup(document, "Notes");
        var draft = CreateGroup(document, "Draft");

        var configuration = new PdfDictionary(document);
        var on = new PdfArray(document);
        var off = new PdfArray(document);
        var order = new PdfArray(document);

        on.Elements.Add(notes.Reference);
        off.Elements.Add(draft.Reference);
        order.Elements.Add(notes.Reference);
        order.Elements.Add(draft.Reference);
        configuration.Elements["/ON"] = on;
        configuration.Elements["/OFF"] = off;
        configuration.Elements["/Order"] = order;

        var groups = new PdfArray(document);

        groups.Elements.Add(notes.Reference);
        groups.Elements.Add(draft.Reference);

        var properties = new PdfDictionary(document);

        properties.Elements["/OCGs"] = groups;
        properties.Elements["/D"] = configuration;
        document.Catalog.Elements["/OCProperties"] = properties;

        var resources = page.Elements.GetDictionary("/Resources");
        var used = new PdfDictionary(document);

        used.Elements["/L1"] = notes.Reference;
        used.Elements["/L2"] = draft.Reference;
        resources.Elements["/Properties"] = used;

        return Save(document);
    }

    /// <summary>
    /// Builds a PDF whose pages each carry their own, identical and uncompressed copy of a form, with a page
    /// thumbnail, as files assembled from separate pieces often do.
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The file.</returns>
    public static byte[] DuplicatedStreamsPdf(int pages = 3)
    {
        using var document = new PdfDocument();

        var drawing = new StringBuilder();

        for (var line = 0; line < 1500; line++)
        {
            drawing.Append(CultureInfo.InvariantCulture, $"{line % 100} 0 m {line % 100} 100 l S\n");
        }

        var content = Encoding.ASCII.GetBytes(drawing.ToString());

        for (var number = 0; number < pages; number++)
        {
            var page = LetterPage(document);
            var canvas = PdfCanvas.ForPage(page);

            canvas.DrawText("Page with a repeated drawing", PdfFontLibrary.Get("Arial"), 12, 72, 100, _black);
            canvas.Commit();

            // The form and the thumbnail are stored uncompressed, as the pieces such files are put together from are.
            var form = new PdfDictionary(document);

            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements["/BBox"] = new PdfRectangle(0, 0, 100, 100);
            form.CreateStream(content);
            document.AddObject(form);

            var thumbnail = new PdfDictionary(document);

            thumbnail.CreateStream(new byte[600]);
            document.AddObject(thumbnail);
            page.Elements["/Thumb"] = thumbnail.Reference;

            var resources = page.Elements.GetDictionary("/Resources");
            var objects = resources.Elements.GetDictionary("/XObject");

            if (objects is null)
            {
                objects = new PdfDictionary(document);
                resources.Elements["/XObject"] = objects;
            }

            objects.Elements["/Fm9"] = form.Reference;
        }

        return Save(document);
    }

    private static PdfDictionary CreateGroup(PdfDocument document, string name)
    {
        var group = new PdfDictionary(document);

        group.Elements.SetName("/Type", "/OCG");
        group.Elements.SetString("/Name", name);
        document.AddObject(group);

        return group;
    }

    private static double DrawBody(PdfCanvas canvas, PdfFont font, double y)
    {
        for (var line = 0; line < 6; line++)
        {
            canvas.DrawText("The quarter closed ahead of plan with steady growth across every region we serve.", font, 10, 72, y, _black);
            y += 14;
        }

        return y + 10;
    }

    /// <summary>
    /// Saves a fixture with an XMP packet that agrees with its document information, as PDF writers do.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The file.</returns>
    private static byte[] Save(PdfDocument document)
    {
        return PdfMetadataSync.Capture(document).Save(document, TimeProvider.System.GetUtcNow());
    }

    private static PdfPage LetterPage(PdfDocument document)
    {
        var page = document.AddPage();

        page.MediaBox = new PdfRectangle(0, 0, 612, 792);

        return page;
    }
}
