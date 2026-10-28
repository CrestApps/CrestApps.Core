using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Adds the objects test PDFs carry beyond their drawing: links, notes and bookmarks, written as a PDF
/// writer would write them.
/// </summary>
internal static class PdfFixtureObjects
{
    /// <summary>
    /// Adds a link that opens an address.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="left">The left edge, in PDF user space.</param>
    /// <param name="bottom">The bottom edge, in PDF user space.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="uri">The address.</param>
    public static void AddWebLink(PdfPage page, double left, double bottom, double width, double height, string uri)
    {
        PdfLinkAnnotations.AddWebLink(page, new PdfRectangle(left, bottom, left + width, bottom + height), uri);
    }

    /// <summary>
    /// Adds a link that goes to a page of the same document.
    /// </summary>
    /// <param name="page">The page the link is on.</param>
    /// <param name="left">The left edge, in PDF user space.</param>
    /// <param name="bottom">The bottom edge, in PDF user space.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="target">The page it goes to.</param>
    public static void AddPageLink(PdfPage page, double left, double bottom, double width, double height, PdfPage target)
    {
        PdfLinkAnnotations.AddPageLink(page, new PdfRectangle(left, bottom, left + width, bottom + height), target);
    }

    /// <summary>
    /// Adds a sticky note.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="left">The left edge, in PDF user space.</param>
    /// <param name="bottom">The bottom edge, in PDF user space.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="author">The note's author.</param>
    /// <param name="contents">The note's text.</param>
    public static void AddNote(PdfPage page, double left, double bottom, double width, double height, string author, string contents)
    {
        var note = Annotation(page, "/Text", left, bottom, width, height);

        note.Elements.SetString("/T", author);
        note.Elements.SetString("/Contents", contents);
    }

    /// <summary>
    /// Adds a bookmark at the end of the document's outline, or of another bookmark's children.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="parent">The bookmark it goes under, or <see langword="null"/> for the top level.</param>
    /// <param name="title">The title.</param>
    /// <param name="target">The page it goes to.</param>
    /// <param name="open">Whether its children show.</param>
    /// <returns>The bookmark.</returns>
    public static PdfDictionary AddBookmark(PdfDocument document, PdfDictionary parent, string title, PdfPage target, bool open = false)
    {
        if (parent is null)
        {
            parent = document.Catalog.Elements.GetDictionary("/Outlines");

            if (parent is null)
            {
                parent = new PdfDictionary(document);
                parent.Elements.SetName("/Type", "/Outlines");
                document.Catalog.Elements.SetReference("/Outlines", parent);
            }
        }

        var item = new PdfDictionary(document);

        item.Elements.SetString("/Title", title);
        item.Elements["/Dest"] = Destination(target);
        item.Elements["/Parent"] = document.AddObject(parent);

        if (open)
        {
            // An open bookmark counts what it shows; one without children counts nothing.
            item.Elements.SetInteger("/Count", 0);
        }

        var reference = document.AddObject(item);

        if (parent.Elements.GetDictionary("/Last") is { } last)
        {
            last.Elements["/Next"] = reference;
            item.Elements["/Prev"] = last.Reference;
        }
        else
        {
            parent.Elements["/First"] = reference;
        }

        parent.Elements["/Last"] = reference;

        for (var node = parent; node is not null; node = node.Elements.GetDictionary("/Parent"))
        {
            var count = node.Elements.GetInteger("/Count");
            var isRoot = string.Equals(node.Elements.GetName("/Type"), "/Outlines", StringComparison.Ordinal);

            // A closed bookmark counts its children negatively and hides the new one from the bookmarks above it.
            if (!isRoot && (!node.Elements.ContainsKey("/Count") || count < 0))
            {
                node.Elements.SetInteger("/Count", count - 1);

                break;
            }

            node.Elements.SetInteger("/Count", count + 1);
        }

        return item;
    }

    private static PdfDictionary Annotation(PdfPage page, string subtype, double left, double bottom, double width, double height)
    {
        var annotation = new PdfDictionary(page.Owner);

        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", subtype);
        annotation.Elements["/Rect"] = new PdfRectangle(left, bottom, left + width, bottom + height);
        annotation.Elements["/P"] = page.Owner.AddObject(page);

        var annotations = page.Elements.GetArray("/Annots");

        if (annotations is null)
        {
            annotations = new PdfArray(page.Owner);
            page.Elements["/Annots"] = annotations;
        }

        annotations.Elements.Add(page.Owner.AddObject(annotation));

        return annotation;
    }

    private static PdfArray Destination(PdfPage target)
    {
        return new PdfArray(target.Owner, target.Owner.AddObject(target), new PdfName("/XYZ"), PdfNull.Value, PdfNull.Value, new PdfInteger(0));
    }
}
