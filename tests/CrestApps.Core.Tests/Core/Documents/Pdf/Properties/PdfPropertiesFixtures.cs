using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

/// <summary>
/// Builds the PDFs the document-property tool tests work on, in memory.
/// </summary>
internal static class PdfPropertiesFixtures
{
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
        PdfFontConfiguration.Ensure();

        using var document = new PdfDocument();

        document.Info.Title = title;
        document.Info.Author = "Old Author";

        var font = new XFont("Arial", 12);

        for (var number = 1; number <= pages; number++)
        {
            var page = document.AddPage();

            using var graphics = XGraphics.FromPdfPage(page);

            graphics.DrawString($"Page {number}: contact us at the help desk for details.", font, XBrushes.Black, new XPoint(72, 100));
            graphics.DrawString("Visit the website for the full report.", font, XBrushes.Black, new XPoint(72, 130));
        }

        if (pdfA)
        {
            document.SetPdfA();
        }

        customize?.Invoke(document);

        return Save(document);
    }

    /// <summary>
    /// Builds a three-page report with a title, two heading levels, body text, a running head and page
    /// numbers.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] HeadingsPdf()
    {
        PdfFontConfiguration.Ensure();

        using var document = new PdfDocument();

        var title = new XFont("Arial", 24, XFontStyleEx.Bold);
        var heading = new XFont("Arial", 18, XFontStyleEx.Bold);
        var subheading = new XFont("Arial", 14, XFontStyleEx.Bold);
        var body = new XFont("Arial", 10);
        var running = new XFont("Arial", 8);

        var sections = new[]
        {
            ("Introduction", new[] { "Background", "Scope" }),
            ("Results", new[] { "Revenue" }),
            ("Outlook", Array.Empty<string>()),
        };

        for (var index = 0; index < sections.Length; index++)
        {
            var page = document.AddPage();

            using var graphics = XGraphics.FromPdfPage(page);

            graphics.DrawString("Contoso Internal Report", running, XBrushes.Gray, new XPoint(72, 30));
            graphics.DrawString($"Page {index + 1}", running, XBrushes.Gray, new XPoint(290, 770));

            var y = 80d;

            if (index == 0)
            {
                graphics.DrawString("Annual Review", title, XBrushes.Black, new XPoint(72, y));
                y += 50;
            }

            var (name, children) = sections[index];

            graphics.DrawString(name, heading, XBrushes.Black, new XPoint(72, y));
            y += 30;
            y = DrawBody(graphics, body, y);

            foreach (var child in children)
            {
                graphics.DrawString(child, subheading, XBrushes.Black, new XPoint(72, y));
                y += 24;
                y = DrawBody(graphics, body, y);
            }
        }

        return Save(document);
    }

    /// <summary>
    /// Builds a one-page PDF with two layers, "Notes" (shown) and "Draft" (hidden), each drawing a line of text.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] LayersPdf()
    {
        PdfFontConfiguration.Ensure();

        using var document = new PdfDocument();

        var page = document.AddPage();

        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawString("Base content", new XFont("Arial", 12), XBrushes.Black, new XPoint(72, 100));
        }

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
        document.Internals.Catalog.Elements["/OCProperties"] = properties;

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
        PdfFontConfiguration.Ensure();

        using var document = new PdfDocument();

        document.Options.CompressContentStreams = false;
        document.Options.NoCompression = true;

        var drawing = new StringBuilder();

        for (var line = 0; line < 1500; line++)
        {
            drawing.Append(CultureInfo.InvariantCulture, $"{line % 100} 0 m {line % 100} 100 l S\n");
        }

        var content = Encoding.ASCII.GetBytes(drawing.ToString());

        for (var number = 0; number < pages; number++)
        {
            var page = document.AddPage();

            using (var graphics = XGraphics.FromPdfPage(page))
            {
                graphics.DrawString("Page with a repeated drawing", new XFont("Arial", 12), XBrushes.Black, new XPoint(72, 100));
            }

            var form = new PdfDictionary(document);

            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements["/BBox"] = new PdfLiteral("[0 0 100 100]");
            form.CreateStream(content);
            document.Internals.AddObject(form);

            var thumbnail = new PdfDictionary(document);

            thumbnail.CreateStream(new byte[600]);
            document.Internals.AddObject(thumbnail);
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
        document.Internals.AddObject(group);

        return group;
    }

    private static double DrawBody(XGraphics graphics, XFont font, double y)
    {
        for (var line = 0; line < 6; line++)
        {
            graphics.DrawString("The quarter closed ahead of plan with steady growth across every region we serve.", font, XBrushes.Black, new XPoint(72, y));
            y += 14;
        }

        return y + 10;
    }

    private static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();

        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }
}
