using System.Text;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Builds the PDFs the tool tests work on.
/// </summary>
internal static class PdfToolFixtures
{
    /// <summary>
    /// Builds a document of paragraphs, one page per entry.
    /// </summary>
    /// <param name="pages">The text of each page.</param>
    /// <returns>The PDF.</returns>
    public static async Task<byte[]> SimplePdfAsync(params string[] pages)
    {
        var definition = new PdfDocumentDefinition { Title = "Fixture" };
        var section = new PdfSectionDefinition { Id = "s1" };

        definition.Sections.Add(section);

        for (var index = 0; index < pages.Length; index++)
        {
            if (index > 0)
            {
                section.Blocks.Add(new PdfBlockDefinition { Id = "break" + index, Type = "page_break" });
            }

            section.Blocks.Add(new PdfBlockDefinition { Id = "p" + index, Type = "paragraph", Text = pages[index] });
        }

        var composer = new PdfDocumentComposer(Options.Create(new PdfCompositionOptions()), TimeProvider.System);

        return (await composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken)).Bytes;
    }

    /// <summary>
    /// Builds a one-page PDF whose text is drawn in the standard Helvetica font, not embedded, the way many
    /// simple generators write it.
    /// </summary>
    /// <param name="content">The page content stream.</param>
    /// <returns>The PDF.</returns>
    public static byte[] StandardFontPdf(string content)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var font = new PdfDictionary(document);

        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        document.Internals.AddObject(font);

        var fonts = new PdfDictionary(document);
        fonts.Elements["/F1"] = font.Reference;
        page.Resources.Elements["/Font"] = fonts;

        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content));

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }
}
