using CrestApps.Core.AI.Documents.Pdf.Objects;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Gives a composed document what PDF/A-2B asks of it beyond its content: an sRGB output intent, and
/// annotations that print.
/// </summary>
/// <remarks>
/// The XMP identification is written by the metadata sync when the file is saved.
/// </remarks>
internal static class PdfArchiveProfile
{
    // PdfPig ships the sRGB IEC61966-2.1 profile for its own PDF/A support.
    private const string ProfileResource = "UglyToad.PdfPig.Resources.ICC.sRGB2014.icc";

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

        using (var stream = typeof(PigDocument).Assembly.GetManifestResourceStream(ProfileResource))
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
        icc.CreateStream([]).SetCompressed(profile);
        document.AddObject(icc);

        var intent = new PdfDictionary(document);

        intent.Elements.SetName("/Type", "/OutputIntent");
        intent.Elements.SetName("/S", "/GTS_PDFA1");
        intent.Elements["/OutputConditionIdentifier"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/Info"] = new PdfString("sRGB IEC61966-2.1");
        intent.Elements["/DestOutputProfile"] = icc.Reference;

        document.Catalog.Elements["/OutputIntents"] = new PdfArray(document, intent);

        foreach (var page in document.Pages)
        {
            foreach (var item in page.Annotations?.Elements ?? [])
            {
                if (PdfObjectModel.Resolve(item) is PdfDictionary annotation)
                {
                    annotation.Elements.SetInteger("/F", PrintFlag);
                }
            }
        }

        return true;
    }
}
