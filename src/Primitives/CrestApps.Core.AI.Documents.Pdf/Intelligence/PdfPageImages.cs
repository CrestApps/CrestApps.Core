using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Reads the pictures embedded in a page and prepares them for a vision model.
/// </summary>
internal static class PdfPageImages
{
    /// <summary>
    /// Returns the images drawn on a page, in the order the page draws them.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The images, or an empty list when the page has none or they cannot be read.</returns>
    public static List<IPdfImage> Get(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        try
        {
            return [.. page.GetImages()];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the area an image covers on the page.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The area in square points.</returns>
    public static double Area(IPdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var box = PdfBox.From(image.BoundingBox);

        return Math.Abs(box.Width * box.Height);
    }

    /// <summary>
    /// Encodes an image as a picture a vision model accepts.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="maxBytes">The largest picture sent.</param>
    /// <param name="bytes">The picture.</param>
    /// <param name="mediaType">The picture's media type.</param>
    /// <param name="problem">Why the image cannot be sent, when it cannot.</param>
    /// <returns><see langword="true"/> when the image was encoded within the limit.</returns>
    public static bool TryEncode(IPdfImage image, int maxBytes, out byte[] bytes, out string mediaType, out string problem)
    {
        ArgumentNullException.ThrowIfNull(image);

        (bytes, mediaType) = PdfPageSvgRenderer.TryEncode(image);
        problem = null;

        if (bytes is null || bytes.Length == 0)
        {
            bytes = null;
            problem = "the image is stored in a form that cannot be converted for a vision model (for example JBIG2 or CCITT fax)";

            return false;
        }

        if (maxBytes > 0 && bytes.Length > maxBytes)
        {
            problem = $"the image is {bytes.Length:N0} bytes, more than the {maxBytes:N0} bytes sent to a vision model";
            bytes = null;

            return false;
        }

        return true;
    }
}
