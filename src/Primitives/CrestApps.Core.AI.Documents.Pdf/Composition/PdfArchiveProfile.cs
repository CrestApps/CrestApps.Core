using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Gives a composed document what PDF/A-2B asks of it beyond its content: an sRGB output intent, and
/// annotations that print.
/// </summary>
/// <remarks>
/// PDFsharp's own PDF/A switch also turns on its PDF/UA structure mode, in which MigraDoc cannot draw, so the
/// composer does not use it; the XMP identification is written by the metadata sync when the file is saved.
/// </remarks>
internal static class PdfArchiveProfile
{
    // PDFsharp ships the sRGB IEC61966-2.1 profile for its own PDF/A support.
    private const string ProfileResource = "PdfSharp.Resources.sRGB2014.icc";

    private const int PrintFlag = 4;

    /// <summary>
    /// Adds the output intent and makes every annotation printable.
    /// </summary>
    /// <param name="document">The rendered document, before it is saved.</param>
    /// <returns><see langword="true"/> when the document now meets these requirements.</returns>
    public static bool TryApply(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        byte[] profile;

        using (var stream = typeof(PdfDocument).Assembly.GetManifestResourceStream(ProfileResource))
        {
            if (stream is null)
            {
                return false;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            profile = buffer.ToArray();
        }

        var icc = new PdfDictionary(document);

        icc.Elements.SetInteger("/N", 3);
        icc.CreateStream(profile);
        document.Internals.AddObject(icc);

        var intent = new PdfDictionary(document);

        intent.Elements.SetName("/Type", "/OutputIntent");
        intent.Elements.SetName("/S", "/GTS_PDFA1");
        intent.Elements["/OutputConditionIdentifier"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/Info"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/DestOutputProfile"] = icc.Reference;

        var intents = new PdfArray(document);
        intents.Elements.Add(intent);
        document.Internals.Catalog.Elements["/OutputIntents"] = intents;

        foreach (var page in document.Pages)
        {
            if (page.Elements.GetObject("/Annots") is not PdfArray annotations)
            {
                continue;
            }

            foreach (var item in annotations.Elements)
            {
                var annotation = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;

                annotation?.Elements.SetInteger("/F", PrintFlag);
            }
        }

        return true;
    }
}
