using System.IO.Compression;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Lists, reads, embeds and removes the files a PDF carries.
/// </summary>
/// <remarks>
/// The embedded-files tree is read and written with <see cref="PdfNameTree"/>, so the files a document
/// already carries are kept, each with its description and media type.
/// </remarks>
internal static class PdfAttachments
{
    /// <summary>
    /// Lists the attachments of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The files of the embedded-files tree, then the page attachments.</returns>
    public static List<PdfAttachment> List(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var attachments = new List<PdfAttachment>();

        foreach (var entry in PdfNameTree.Read(GetTree(document)))
        {
            if (PdfObjects.Resolve(entry.Value) is PdfDictionary specification)
            {
                attachments.Add(Describe(entry.Key, specification, null));
            }
        }

        for (var index = 0; index < document.PageCount; index++)
        {
            var annotations = PdfObjects.GetArray(document.Pages[index], "/Annots");

            if (annotations is null)
            {
                continue;
            }

            foreach (var item in annotations.Elements)
            {
                if (PdfObjects.Resolve(item) is not PdfDictionary annotation ||
                    !string.Equals(PdfObjects.GetName(annotation, "/Subtype"), "FileAttachment", StringComparison.Ordinal) ||
                    PdfObjects.GetDictionary(annotation, "/FS") is not { } specification)
                {
                    continue;
                }

                var attachment = Describe(null, specification, index + 1);

                attachment.Name = attachment.FileName ?? "attachment";
                attachment.Description ??= PdfObjects.GetText(annotation, "/Contents");
                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    /// <summary>
    /// Reads the bytes of an attachment.
    /// </summary>
    /// <param name="attachment">The attachment.</param>
    /// <returns>The file.</returns>
    public static byte[] Read(PdfAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (attachment.Stream?.Stream is null)
        {
            throw new PdfToolException($"\"{attachment.Name}\" refers to a file outside the PDF; there is nothing embedded to extract.");
        }

        var raw = attachment.Stream.Stream.Value;
        var filter = attachment.Stream.Elements["/Filter"];

        if (filter is null)
        {
            return raw;
        }

        if (!PdfStreamFilters.TryDecode(attachment.Stream, raw, out var decoded))
        {
            throw new PdfToolException($"\"{attachment.Name}\" is stored with a compression this tool cannot decode ({filter}).");
        }

        return decoded;
    }

    /// <summary>
    /// Embeds a file in a document's embedded-files tree, replacing a file listed under the same name.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="name">The name to list the file under.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="description">The description, or <see langword="null"/>.</param>
    /// <param name="mediaType">The media type, or <see langword="null"/>.</param>
    /// <param name="now">The time of the change.</param>
    /// <param name="associate">Whether the file is also listed as an associated file of the document, as PDF/A-3 requires.</param>
    /// <returns><see langword="true"/> when a file of the same name was replaced.</returns>
    public static bool Add(PdfDocument document, string name, byte[] bytes, string description, string mediaType, DateTimeOffset now, bool associate)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var compressed = PdfStream.Compress(bytes, CompressionLevel.SmallestSize);
        var stream = new PdfDictionary(document);

        stream.Elements.SetName("/Type", "/EmbeddedFile");

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            stream.Elements["/Subtype"] = new PdfName("/" + mediaType.Trim());
        }

        var parameters = new PdfDictionary(document);

        parameters.Elements.SetInteger("/Size", bytes.Length);
        parameters.Elements.SetDateTime("/CreationDate", now.UtcDateTime);
        parameters.Elements.SetDateTime("/ModDate", now.UtcDateTime);
        stream.Elements["/Params"] = parameters;

        if (compressed.Length < bytes.Length)
        {
            stream.CreateStream(compressed);
            stream.Elements.SetName("/Filter", "/FlateDecode");
        }
        else
        {
            stream.CreateStream(bytes);
        }

        document.AddObject(stream);

        var specification = new PdfDictionary(document);
        var files = new PdfDictionary(document);

        files.Elements.SetReference("/F", stream);
        files.Elements.SetReference("/UF", stream);
        specification.Elements.SetName("/Type", "/Filespec");
        specification.Elements.SetString("/F", name);
        specification.Elements.SetString("/UF", name);
        specification.Elements["/EF"] = files;

        if (!string.IsNullOrWhiteSpace(description))
        {
            specification.Elements.SetString("/Desc", description.Trim());
        }

        if (associate)
        {
            specification.Elements.SetName("/AFRelationship", "/Unspecified");
        }

        document.AddObject(specification);

        var entries = PdfNameTree.Read(GetTree(document));
        var replaced = entries.RemoveAll(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase));

        entries.Add(new KeyValuePair<string, PdfItem>(name, specification.Reference));
        SetTree(document, entries);

        if (associate)
        {
            var catalog = document.Catalog;
            var associated = PdfObjects.GetArray(catalog, "/AF");

            if (associated is null)
            {
                associated = new PdfArray(document);
                catalog.Elements["/AF"] = associated;
            }

            associated.Elements.Add(specification.Reference);
        }

        return replaced > 0;
    }

    /// <summary>
    /// Removes every attachment that answers to a name, from the embedded-files tree and from the pages.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="name">The listed name or file name.</param>
    /// <returns>The number of attachments removed.</returns>
    public static int Remove(PdfDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);

        var removed = new List<PdfDictionary>();
        var entries = PdfNameTree.Read(GetTree(document));
        var kept = new List<KeyValuePair<string, PdfItem>>();

        foreach (var entry in entries)
        {
            var specification = PdfObjects.Resolve(entry.Value) as PdfDictionary;

            if (specification is not null && Describe(entry.Key, specification, null).Matches(name))
            {
                removed.Add(specification);

                continue;
            }

            kept.Add(entry);
        }

        if (removed.Count > 0)
        {
            SetTree(document, kept);
        }

        for (var index = 0; index < document.PageCount; index++)
        {
            var annotations = PdfObjects.GetArray(document.Pages[index], "/Annots");

            if (annotations is null)
            {
                continue;
            }

            for (var position = annotations.Elements.Count - 1; position >= 0; position--)
            {
                if (PdfObjects.Resolve(annotations.Elements[position]) is PdfDictionary annotation &&
                    string.Equals(PdfObjects.GetName(annotation, "/Subtype"), "FileAttachment", StringComparison.Ordinal) &&
                    PdfObjects.GetDictionary(annotation, "/FS") is { } specification &&
                    Describe(null, specification, index + 1).Matches(name))
                {
                    annotations.Elements.RemoveAt(position);
                    removed.Add(specification);
                }
            }
        }

        var associated = PdfObjects.GetArray(document.Catalog, "/AF");

        if (associated is not null)
        {
            for (var position = associated.Elements.Count - 1; position >= 0; position--)
            {
                if (removed.Any(specification => PdfObjects.SameObject(associated.Elements[position], specification)))
                {
                    associated.Elements.RemoveAt(position);
                }
            }
        }

        return removed.Count;
    }

    private static PdfAttachment Describe(string name, PdfDictionary specification, int? page)
    {
        var files = PdfObjects.GetDictionary(specification, "/EF");
        var stream = PdfObjects.GetDictionary(files, "/UF") ?? PdfObjects.GetDictionary(files, "/F");
        var parameters = PdfObjects.GetDictionary(stream, "/Params");
        var size = PdfObjects.GetInteger(parameters, "/Size");

        if (size is null && stream?.Stream is not null && stream.Elements["/Filter"] is null)
        {
            size = stream.Stream.Length;
        }

        var fileName = PdfObjects.GetText(specification, "/UF") ?? PdfObjects.GetText(specification, "/F");

        return new PdfAttachment
        {
            Name = name ?? fileName,
            FileName = fileName,
            Description = PdfObjects.GetText(specification, "/Desc"),
            Size = size,
            MediaType = PdfObjects.GetName(stream, "/Subtype"),
            Page = page,
            FileSpecification = specification,
            Stream = stream,
        };
    }

    private static PdfDictionary GetTree(PdfDocument document)
    {
        return PdfObjects.GetDictionary(PdfObjects.GetDictionary(document.Catalog, "/Names"), "/EmbeddedFiles");
    }

    private static void SetTree(PdfDocument document, List<KeyValuePair<string, PdfItem>> entries)
    {
        var catalog = document.Catalog;
        var names = PdfObjects.GetDictionary(catalog, "/Names");

        if (names is null)
        {
            if (entries.Count == 0)
            {
                return;
            }

            names = new PdfDictionary(document);
            document.AddObject(names);
            catalog.Elements.SetReference("/Names", names);
        }

        if (entries.Count == 0)
        {
            names.Elements.Remove("/EmbeddedFiles");

            return;
        }

        var tree = PdfNameTree.Build(document, entries);

        names.Elements.SetReference("/EmbeddedFiles", tree);
    }
}
