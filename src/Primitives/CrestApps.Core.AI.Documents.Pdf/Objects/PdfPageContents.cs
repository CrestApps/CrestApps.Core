namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The content streams of a page, which a viewer reads one after the other as one.
/// </summary>
internal sealed class PdfPageContents
{
    private readonly PdfPage _page;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPageContents"/> class.
    /// </summary>
    /// <param name="page">The page.</param>
    public PdfPageContents(PdfPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Gets the page's content streams, in the order they are drawn.
    /// </summary>
    public List<PdfDictionary> Streams => [.. Normalize().Elements.Select(PdfObjectModel.Resolve).OfType<PdfDictionary>().Where(stream => stream.Stream is not null)];

    /// <summary>
    /// Adds an empty content stream drawn after the existing ones.
    /// </summary>
    /// <returns>The stream's dictionary, to give data with <see cref="PdfDictionary.CreateStream"/>.</returns>
    public PdfDictionary AppendContent()
    {
        var stream = CreateStream();

        Normalize().Elements.Add(stream.Reference);

        return stream;
    }

    /// <summary>
    /// Adds an empty content stream drawn before the existing ones.
    /// </summary>
    /// <returns>The stream's dictionary, to give data with <see cref="PdfDictionary.CreateStream"/>.</returns>
    public PdfDictionary PrependContent()
    {
        var stream = CreateStream();

        Normalize().Elements.Insert(0, stream.Reference);

        return stream;
    }

    /// <summary>
    /// Replaces every content stream with one holding the given content.
    /// </summary>
    /// <param name="content">The content, unencoded.</param>
    public void ReplaceContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stream = CreateStream();

        stream.CreateStream([]).SetCompressed(content);
        _page.Elements["/Contents"] = stream.Reference;
    }

    /// <summary>
    /// Reads the page's content as one, every stream decoded and the streams separated by a line break.
    /// </summary>
    /// <returns>The content.</returns>
    public byte[] ReadAll()
    {
        using var buffer = new MemoryStream();

        foreach (var stream in Streams)
        {
            buffer.Write(stream.Stream.UnfilteredValue);
            buffer.WriteByte((byte)'\n');
        }

        return buffer.ToArray();
    }

    private PdfDictionary CreateStream()
    {
        var stream = new PdfDictionary(_page.Owner);

        stream.CreateStream([]);
        _page.Owner.AddObject(stream);

        return stream;
    }

    private PdfArray Normalize()
    {
        var current = _page.Elements["/Contents"];

        if (PdfObjectModel.Resolve(current) is PdfArray array)
        {
            if (current is PdfReference)
            {
                // A shared array of streams is copied, so adding to this page's content leaves other pages as they are.
                array = new PdfArray(_page.Owner, array.Elements);
                _page.Elements["/Contents"] = array;
            }

            return array;
        }

        var created = new PdfArray(_page.Owner);

        switch (current)
        {
            case PdfReference reference when reference.Value is PdfDictionary { Stream: not null }:
                created.Elements.Add(reference);

                break;

            case PdfDictionary { Stream: not null } direct:
                created.Elements.Add(_page.Owner.AddObject(direct));

                break;
        }

        _page.Elements["/Contents"] = created;

        return created;
    }
}
