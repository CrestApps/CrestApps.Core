using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Adds link annotations to a page: areas that open an address or go to another page when clicked.
/// </summary>
/// <remarks>
/// The links have no visible border, so the text they sit on looks as it did.
/// </remarks>
internal static class PdfLinkAnnotations
{
    /// <summary>
    /// Adds a link that opens an address: a web page, an email address or a phone number.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="area">The area that is clicked, in PDF user space.</param>
    /// <param name="uri">The address.</param>
    /// <returns>The link annotation.</returns>
    public static PdfDictionary AddWebLink(PdfPage page, PdfRectangle area, string uri)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        var action = new PdfDictionary(page.Owner);

        action.Elements.SetName("/S", "/URI");
        action.Elements.SetString("/URI", uri);

        var link = Add(page, area);

        link.Elements["/A"] = action;

        return link;
    }

    /// <summary>
    /// Adds a link that goes to a page of the same document, showing it from its top.
    /// </summary>
    /// <param name="page">The page the link is on.</param>
    /// <param name="area">The area that is clicked, in PDF user space.</param>
    /// <param name="target">The page it goes to.</param>
    /// <returns>The link annotation.</returns>
    public static PdfDictionary AddPageLink(PdfPage page, PdfRectangle area, PdfPage target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var link = Add(page, area);

        link.Elements["/Dest"] = new PdfArray(page.Owner, page.Owner.AddObject(target), new PdfName("/XYZ"), PdfNull.Value, PdfNull.Value, new PdfInteger(0));

        return link;
    }

    private static PdfDictionary Add(PdfPage page, PdfRectangle area)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(area);

        var document = page.Owner;
        var link = new PdfDictionary(document);

        link.Elements.SetName("/Type", "/Annot");
        link.Elements.SetName("/Subtype", "/Link");
        link.Elements["/Rect"] = area;
        link.Elements["/Border"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));
        link.Elements["/P"] = document.AddObject(page);

        var annotations = page.Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(document);
            page.Elements["/Annots"] = annotations;
        }

        annotations.Elements.Add(document.AddObject(link));

        return link;
    }
}
