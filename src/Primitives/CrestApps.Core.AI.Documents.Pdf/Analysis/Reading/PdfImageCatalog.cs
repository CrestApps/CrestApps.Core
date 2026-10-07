using System.Security.Cryptography;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Lists the pictures placed on a page with what the file records about each: size, colour, encoding and
/// where it is drawn.
/// </summary>
internal static class PdfImageCatalog
{
    /// <summary>
    /// Lists the pictures on a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The pictures, in the order the page draws them.</returns>
    public static List<PdfImageEntry> Read(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var entries = new List<PdfImageEntry>();
        List<IPdfImage> images;

        try
        {
            images = [.. page.GetImages()];
        }
        catch (Exception)
        {
            return entries;
        }

        foreach (var image in images)
        {
            var raw = image.RawMemory;

            entries.Add(new PdfImageEntry
            {
                Page = page.Number,
                IndexOnPage = entries.Count + 1,
                Box = PdfBox.From(image.BoundingBox),
                PixelWidth = image.WidthInSamples,
                PixelHeight = image.HeightInSamples,
                BitsPerComponent = image.BitsPerComponent,
                ColorSpace = ReadColorSpace(image),
                Format = ReadFormat(image),
                ByteLength = raw.Length,
                IsInline = image.IsInlineImage,
                IsMask = image.IsImageMask,
                Hash = Convert.ToHexStringLower(SHA256.HashData(raw.Span))[..16],
                Image = image,
            });
        }

        return entries;
    }

    /// <summary>
    /// Returns the file extension a picture is exported with.
    /// </summary>
    /// <param name="mediaType">The media type of the exported bytes.</param>
    /// <returns><c>.jpg</c> or <c>.png</c>.</returns>
    public static string ExtensionFor(string mediaType)
    {
        return string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : ".png";
    }

    private static string ReadColorSpace(IPdfImage image)
    {
        try
        {
            var details = image.ColorSpaceDetails;

            if (details is not null)
            {
                return details.Type == details.BaseType
                    ? details.Type.ToString()
                    : details.Type + " (" + details.BaseType + ")";
            }
        }
        catch (Exception)
        {
            // A colour space PdfPig cannot interpret is reported by the name the file gives it.
        }

        if (image.ImageDictionary is not null &&
            (image.ImageDictionary.TryGet(NameToken.Create("ColorSpace"), out var token) || image.ImageDictionary.TryGet(NameToken.Create("CS"), out token)))
        {
            return token switch
            {
                NameToken name => name.Data,
                ArrayToken { Data.Count: > 0 } array when array.Data[0] is NameToken first => first.Data,
                _ => null,
            };
        }

        return image.IsImageMask
            ? "mask"
            : null;
    }

    private static string ReadFormat(IPdfImage image)
    {
        var filter = LastFilter(image);

        return filter switch
        {
            "DCTDecode" or "DCT" => "jpeg",
            "JPXDecode" => "jpeg2000",
            "JBIG2Decode" => "jbig2",
            "CCITTFaxDecode" or "CCF" => "ccitt",
            _ => "png",
        };
    }

    private static string LastFilter(IPdfImage image)
    {
        var dictionary = image.ImageDictionary;

        if (dictionary is null ||
            (!dictionary.TryGet(NameToken.Create("Filter"), out var token) && !dictionary.TryGet(NameToken.Create("F"), out token)))
        {
            return null;
        }

        // The last filter of a chain is the one the stored picture is encoded with.
        return token switch
        {
            NameToken name => name.Data,
            ArrayToken { Data.Count: > 0 } array when array.Data[^1] is NameToken last => last.Data,
            _ => null,
        };
    }
}
