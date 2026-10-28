using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What a signature records and where it shows.
/// </summary>
internal sealed class PdfSignatureRequest
{
    /// <summary>
    /// Gets or sets the zero-based page the signature field is on.
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Gets or sets the visible box, in PDF user space, or <see langword="null"/> for an invisible signature.
    /// </summary>
    public PdfRectangle Box { get; set; }

    /// <summary>
    /// Gets or sets the reason for signing.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// Gets or sets where the document was signed.
    /// </summary>
    public string Location { get; set; }

    /// <summary>
    /// Gets or sets how to reach the signer.
    /// </summary>
    public string ContactInfo { get; set; }

    /// <summary>
    /// Gets or sets the time of signing.
    /// </summary>
    public DateTimeOffset SigningTime { get; set; }
}

/// <summary>
/// Writes a digitally signed copy of a document: a signature field whose value covers every byte of the file
/// except the signature itself.
/// </summary>
/// <remarks>
/// The file is written with room for the signature, the byte ranges around that room are filled in, and the
/// signature over those ranges is written into the room — the detached PKCS#7 form every PDF validator reads.
/// </remarks>
internal static class PdfSignatureWriter
{
    // Placeholder byte offsets wide enough for any file this writes; they are replaced, padded to the same width.
    private const long Placeholder = 9_999_999_999;

    /// <summary>
    /// Signs a document.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="signer">Makes the signature.</param>
    /// <param name="request">What the signature records and where it shows.</param>
    /// <returns>The signed file.</returns>
    public static async Task<byte[]> SignAsync(PdfDocument document, PdfCmsSigner signer, PdfSignatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(request);

        var size = await signer.GetSignatureSizeAsync();
        var signature = new PdfDictionary(document);

        signature.Elements.SetName("/Type", "/Sig");
        signature.Elements.SetName("/Filter", "/Adobe.PPKLite");
        signature.Elements.SetName("/SubFilter", "/adbe.pkcs7.detached");
        signature.Elements["/ByteRange"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(Placeholder), new PdfInteger(Placeholder), new PdfInteger(Placeholder));
        signature.Elements["/Contents"] = new PdfString(new byte[size], isHex: true);
        signature.Elements.SetString("/Name", signer.CertificateName);
        signature.Elements.SetDateTime("/M", request.SigningTime);
        SetText(signature, "/Reason", request.Reason);
        SetText(signature, "/Location", request.Location);
        SetText(signature, "/ContactInfo", request.ContactInfo);

        var application = new PdfDictionary(document);
        var build = new PdfDictionary(document);

        application.Elements.SetName("/Name", "/CrestApps");
        build.Elements["/App"] = application;
        signature.Elements["/Prop_Build"] = build;
        document.AddObject(signature);

        AddField(document, signature, signer.CertificateName, request);

        var bytes = PdfObjectWriter.Write(document);
        var (contentsStart, contentsEnd, rangeStart, rangeLength) = Locate(bytes, size);

        // The signature covers everything but its own hexadecimal string, angle brackets included.
        var range = string.Create(CultureInfo.InvariantCulture, $"0 {contentsStart} {contentsEnd} {bytes.Length - contentsEnd}").PadRight(rangeLength);

        Encoding.ASCII.GetBytes(range, bytes.AsSpan(rangeStart, rangeLength));

        var covered = new byte[contentsStart + (bytes.Length - contentsEnd)];

        bytes.AsSpan(0, contentsStart).CopyTo(covered);
        bytes.AsSpan(contentsEnd).CopyTo(covered.AsSpan(contentsStart));

        var cms = await signer.GetSignatureAsync(covered);

        if (cms.Length > size)
        {
            throw new InvalidOperationException("The signature is larger than the room kept for it.");
        }

        Encoding.ASCII.GetBytes(Convert.ToHexString(cms), bytes.AsSpan(contentsStart + 1));

        return bytes;
    }

    private static void AddField(PdfDocument document, PdfDictionary signature, string signerName, PdfSignatureRequest request)
    {
        var page = document.Pages[request.PageIndex];
        var form = document.Catalog.Elements.GetDictionary("/AcroForm");

        if (form is null)
        {
            form = new PdfDictionary(document);
            document.Catalog.Elements.SetReference("/AcroForm", form);
        }

        var fields = form.Elements.GetArray("/Fields");

        if (fields is null)
        {
            fields = new PdfArray(document);
            form.Elements["/Fields"] = fields;
        }

        var names = fields.Elements
            .Select(PdfObjectModel.Resolve)
            .OfType<PdfDictionary>()
            .Select(field => field.Elements.GetString("/T"))
            .ToHashSet(StringComparer.Ordinal);

        var number = 1;

        while (names.Contains("Signature" + number.ToString(CultureInfo.InvariantCulture)))
        {
            number++;
        }

        var box = request.Box ?? new PdfRectangle(0, 0, 0, 0);
        var widget = new PdfDictionary(document);

        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Sig");
        widget.Elements.SetString("/T", "Signature" + number.ToString(CultureInfo.InvariantCulture));
        widget.Elements.SetReference("/V", signature);
        widget.Elements["/Rect"] = box;

        // Printed with the page, and locked so a viewer does not let it be moved or changed.
        widget.Elements.SetInteger("/F", 132);
        widget.Elements.SetReference("/P", page);

        var appearance = new PdfDictionary(document);

        appearance.Elements.SetReference("/N", Appearance(document, box, signerName, request));
        widget.Elements["/AP"] = appearance;
        document.AddObject(widget);

        var annotations = page.Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(document);
            page.Elements["/Annots"] = annotations;
        }

        annotations.Elements.Add(widget.Reference);
        fields.Elements.Add(widget.Reference);

        // The document has signatures, and changes must be appended so they do not break them.
        form.Elements.SetInteger("/SigFlags", 3);
    }

    private static PdfDictionary Appearance(PdfDocument document, PdfRectangle box, string signerName, PdfSignatureRequest request)
    {
        var canvas = PdfCanvas.ForForm(document, Math.Max(box.Width, 1), Math.Max(box.Height, 1), out var form);

        if (box.Width < 20 || box.Height < 10)
        {
            canvas.Commit();

            return form;
        }

        var lines = new List<string> { "Digitally signed by " + signerName, "Date: " + request.SigningTime.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) };

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            lines.Add("Reason: " + request.Reason.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            lines.Add("Location: " + request.Location.Trim());
        }

        var font = PdfFontLibrary.Get(PdfFontFamilies.Default);
        var padding = 4d;
        var size = Math.Clamp((box.Height - (2 * padding)) / (lines.Count * 1.25), 4, 10);
        var widest = lines.Max(line => font.Measure(line, 1));

        // The text shrinks to fit the box's width as well as its height.
        size = Math.Max(3, Math.Min(size, (box.Width - (2 * padding)) / Math.Max(widest, 1)));

        canvas.DrawRectangle(0.5, 0.5, box.Width - 1, box.Height - 1, null, new PdfPen(new PdfColor(0x60, 0x60, 0x60), 0.75));

        for (var index = 0; index < lines.Count; index++)
        {
            canvas.DrawText(lines[index], font, size, padding, padding + (size * (1.25 * index + 1)), new PdfBrush(new PdfColor(0, 0, 0)));
        }

        canvas.Commit();

        return form;
    }

    private static (int ContentsStart, int ContentsEnd, int RangeStart, int RangeLength) Locate(byte[] bytes, int size)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var placeholder = string.Create(CultureInfo.InvariantCulture, $"[0 {Placeholder} {Placeholder} {Placeholder}]");
        var rangeAt = text.IndexOf(placeholder, StringComparison.Ordinal);

        if (rangeAt < 0 || text.IndexOf(placeholder, rangeAt + 1, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("The room for the signature could not be found in the written file.");
        }

        var room = "<" + new string('0', size * 2) + ">";
        var contentsAt = text.IndexOf("/Contents " + room, rangeAt, StringComparison.Ordinal);

        if (contentsAt < 0)
        {
            throw new InvalidOperationException("The room for the signature could not be found in the written file.");
        }

        var start = contentsAt + "/Contents ".Length;

        return (start, start + room.Length, rangeAt + 1, placeholder.Length - 2);
    }

    private static void SetText(PdfDictionary dictionary, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary.Elements.SetString(key, value.Trim());
        }
    }
}
