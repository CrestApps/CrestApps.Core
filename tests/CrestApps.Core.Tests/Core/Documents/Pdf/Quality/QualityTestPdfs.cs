using System.Globalization;
using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PdfSharp;
using PdfSharp.Pdf;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

/// <summary>
/// Builds the PDFs the quality tool tests check: clean documents composed with MigraDoc, PDFsharp documents
/// with hand-written content streams, and raw files written object by object to carry exact defects.
/// </summary>
internal static class QualityTestPdfs
{
    /// <summary>
    /// A small XMP packet that claims PDF/A-2b, with a title.
    /// </summary>
    public const string PdfA2bXmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"><pdfaid:part>2</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance></rdf:Description><rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Archive copy</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    /// <summary>
    /// Composes a definition with the default host options.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The PDF.</returns>
    public static async Task<byte[]> ComposeAsync(PdfDocumentDefinition definition)
    {
        var composer = new PdfDocumentComposer(Options.Create(new PdfCompositionOptions()), TimeProvider.System);

        return (await composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken)).Bytes;
    }

    /// <summary>
    /// A clean report: title, language, cover, contents, running heads, page numbers, headings, a table and a chart.
    /// </summary>
    /// <returns>The definition.</returns>
    public static PdfDocumentDefinition Report()
    {
        return new PdfDocumentDefinition
        {
            Title = "Quarterly Report",
            Author = "Analytics Team",
            Language = "en-US",
            PageSetup = new PdfPageSetupDefinition { Size = "A4" },
            CoverPage = new PdfCoverPageDefinition { Enabled = true, Subtitle = "Third quarter" },
            TableOfContents = new PdfTableOfContentsDefinition { Enabled = true },
            Header = new PdfHeaderFooterDefinition { Left = "{title}", Right = "Internal" },
            PageNumbers = new PdfPageNumbersDefinition { Enabled = true },
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Type = "heading", Text = "Summary", Level = 1 },
                        new PdfBlockDefinition { Id = "b2", Type = "paragraph", Text = "Revenue rose by **12%** against plan across every region we operate in." },
                        new PdfBlockDefinition
                        {
                            Id = "b3",
                            Type = "table",
                            Table = new PdfTableDefinition
                            {
                                Columns = [new PdfTableColumnDefinition { Header = "Region" }, new PdfTableColumnDefinition { Header = "Revenue" }],
                                Rows = [["North", "100"], ["South", "200"]],
                            },
                        },
                        new PdfBlockDefinition { Id = "b4", Type = "heading", Text = "Outlook", Level = 1 },
                        new PdfBlockDefinition { Id = "b5", Type = "paragraph", Text = "We expect the next quarter to continue the trend." },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// Adds a composed document to the conversation's workspace, the way create_pdf does.
    /// </summary>
    /// <param name="host">The test host.</param>
    /// <param name="name">The working PDF's name.</param>
    /// <param name="definition">The definition.</param>
    /// <returns>A task that completes when the workspace is saved.</returns>
    public static async Task AddComposedAsync(PdfToolTestHost host, string name, PdfDocumentDefinition definition)
    {
        var store = host.Services.GetRequiredService<IPdfWorkspaceStore>();
        var scope = new PdfWorkspaceScope(host.Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction);
        var state = await store.LoadAsync(scope, TestContext.Current.CancellationToken);

        state.Documents.Add(new PdfWorkingDocument
        {
            Name = name,
            Kind = PdfWorkingDocument.ComposedKind,
            Definition = definition,
            Version = 1,
        });

        state.ActiveDocument = name;

        await store.SaveAsync(scope, state, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Builds a PDF with PDFsharp whose pages draw hand-written content in the standard Helvetica font, which
    /// is not embedded.
    /// </summary>
    /// <param name="pages">The content stream of each page.</param>
    /// <param name="configure">Changes the document before it is saved.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Helvetica(IReadOnlyList<string> pages, Action<PdfDocument> configure = null)
    {
        using var document = new PdfDocument();

        foreach (var content in pages)
        {
            var page = document.AddPage();
            page.Size = PageSize.Letter;

            page.Width = PdfSharp.Drawing.XUnit.FromPoint(612);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(792);

            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            document.Internals.AddObject(font);

            var fonts = new PdfDictionary(document);
            fonts.Elements.SetReference("/F1", font);

            var resources = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;
            page.Elements["/Resources"] = resources;

            if (!string.IsNullOrEmpty(content))
            {
                page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content));
            }
        }

        configure?.Invoke(document);

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    /// <summary>
    /// Adds a link annotation to a page.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="rect">The link's rectangle: left, bottom, right, top.</param>
    /// <param name="action">The action dictionary's entries, for example <c>/S /URI /URI (https://example.com)</c> as name/value pairs.</param>
    public static void AddLink(PdfDocument document, int page, double[] rect, Action<PdfDictionary> action)
    {
        var annotation = new PdfDictionary(document);
        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", "/Link");
        annotation.Elements["/Rect"] = new PdfArray(document, [.. rect.Select(value => new PdfReal(value))]);

        var dictionary = new PdfDictionary(document);
        action(dictionary);
        annotation.Elements["/A"] = dictionary;

        document.Internals.AddObject(annotation);

        var annotations = document.Pages[page].Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(document);
            document.Pages[page].Elements["/Annots"] = annotations;
        }

        annotations.Elements.Add(annotation.Reference);
    }

    /// <summary>
    /// Adds an AcroForm text field with a widget on a page.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="name">The field name.</param>
    public static void AddTextField(PdfDocument document, int page, string name)
    {
        var field = new PdfDictionary(document);
        field.Elements.SetName("/Type", "/Annot");
        field.Elements.SetName("/Subtype", "/Widget");
        field.Elements.SetName("/FT", "/Tx");
        field.Elements.SetString("/T", name);
        field.Elements["/Rect"] = new PdfArray(document, new PdfReal(72), new PdfReal(600), new PdfReal(272), new PdfReal(620));
        field.Elements.SetReference("/P", document.Pages[page]);
        document.Internals.AddObject(field);

        var annotations = document.Pages[page].Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(document);
            document.Pages[page].Elements["/Annots"] = annotations;
        }

        annotations.Elements.Add(field.Reference);

        var form = new PdfDictionary(document);
        form.Elements["/Fields"] = new PdfArray(document, field.Reference);
        document.Internals.Catalog.Elements["/AcroForm"] = form;
    }

    /// <summary>
    /// Writes a PDF object by object with a correct cross-reference table. Object 1 must be the catalog.
    /// </summary>
    /// <param name="objects">The bodies of objects 1, 2, 3 and so on.</param>
    /// <param name="trailer">Extra trailer entries, for example an /ID or /Info.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Raw(IReadOnlyList<string> objects, string trailer = "")
    {
        var builder = new StringBuilder("%PDF-1.7\n%âãÏÓ\n");
        var offsets = new List<int>();

        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R {trailer} >>\nstartxref\n{xref}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Writes a stream object body.
    /// </summary>
    /// <param name="content">The stream's content.</param>
    /// <param name="entries">Extra dictionary entries.</param>
    /// <returns>The object body.</returns>
    public static string Stream(string content, string entries = "")
    {
        return string.Create(CultureInfo.InvariantCulture, $"<< /Length {Encoding.Latin1.GetByteCount(content)} {entries} >>\nstream\n{content}\nendstream");
    }

    /// <summary>
    /// Appends an incremental update that replaces one object.
    /// </summary>
    /// <param name="original">The original file, as <see cref="Raw"/> writes it.</param>
    /// <param name="objectNumber">The object to replace.</param>
    /// <param name="body">Its new body.</param>
    /// <param name="size">The trailer /Size.</param>
    /// <returns>The updated file.</returns>
    public static byte[] AppendUpdate(byte[] original, int objectNumber, string body, int size)
    {
        var text = Encoding.Latin1.GetString(original);
        var startXref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var previous = text[(startXref + "startxref".Length)..].Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        var builder = new StringBuilder(text);
        var offset = builder.Length;

        builder.Append(CultureInfo.InvariantCulture, $"{objectNumber} 0 obj\n{body}\nendobj\n");

        var xref = builder.Length;

        builder.Append(CultureInfo.InvariantCulture, $"xref\n{objectNumber} 1\n{offset:D10} 00000 n \n");
        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root 1 0 R /Prev {previous} >>\nstartxref\n{xref}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Writes a tagged one-page PDF whose structure has a figure without alternative text, a table without
    /// header cells and a heading; the page draws a filled square and no text.
    /// </summary>
    /// <param name="marked">Whether /MarkInfo /Marked is set.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Tagged(bool marked)
    {
        var markInfo = marked
            ? "/MarkInfo << /Marked true >>"
            : string.Empty;

        return Raw(
        [
            $"<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /Lang (en-GB) {markInfo} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            Stream("/Figure <</MCID 0>> BDC 1 0 0 rg 72 600 100 100 re f EMC"),
            "<< /Type /StructTreeRoot /K 6 0 R >>",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R 8 0 R 9 0 R] >>",
            "<< /Type /StructElem /S /H1 /P 6 0 R /K [] >>",
            "<< /Type /StructElem /S /Figure /P 6 0 R /Pg 3 0 R /K 0 >>",
            "<< /Type /StructElem /S /Table /P 6 0 R /K [10 0 R] >>",
            "<< /Type /StructElem /S /TR /P 9 0 R /K [11 0 R] >>",
            "<< /Type /StructElem /S /TD /P 10 0 R /K [] >>",
        ],
            "/ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>]");
    }

    /// <summary>
    /// Writes a one-page PDF that claims PDF/A-2b and meets its main requirements: XMP identification, an
    /// output intent with a profile, a document ID, matching metadata and no text (so no fonts).
    /// </summary>
    /// <returns>The PDF.</returns>
    public static byte[] ArchiveCandidate()
    {
        return Raw(
        [
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R /OutputIntents [6 0 R] >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            Stream("0 0 1 rg 72 600 200 100 re f"),
            Stream(PdfA2bXmp, "/Type /Metadata /Subtype /XML"),
            "<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (sRGB IEC61966-2.1) /DestOutputProfile 7 0 R >>",
            Stream("not a real profile", "/N 3"),
            "<< /Title (Archive copy) >>",
        ],
            "/Info 8 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>]");
    }
}
