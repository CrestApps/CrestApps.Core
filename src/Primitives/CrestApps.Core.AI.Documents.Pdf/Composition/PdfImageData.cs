namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The bytes of a picture a composed document places, with what is known about them.
/// </summary>
/// <param name="Bytes">The encoded image.</param>
/// <param name="MediaType">The media type, such as <c>image/png</c>.</param>
/// <param name="Name">A name to refer to the picture by in messages.</param>
internal sealed record PdfImageData(byte[] Bytes, string MediaType, string Name)
{
    /// <summary>
    /// Gets a value indicating whether the picture is in a format the PDF renderer can place: JPEG, PNG,
    /// BMP or GIF.
    /// </summary>
    public bool IsSupported => PdfImageInfo.TryRead(Bytes, out _, out _, out _);
}
