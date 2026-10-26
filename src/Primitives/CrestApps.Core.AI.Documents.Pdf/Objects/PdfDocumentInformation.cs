namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The document information dictionary: title, author, subject, keywords, creator, producer and dates.
/// </summary>
/// <remarks>
/// The dictionary is created the first time a value is set, so a document that never sets one writes none.
/// </remarks>
internal sealed class PdfDocumentInformation
{
    private readonly PdfDocument _document;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocumentInformation"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfDocumentInformation(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Gets the dictionary, or <see langword="null"/> when the document has none.
    /// </summary>
    public PdfDictionary Dictionary => _document.InfoReference?.Value as PdfDictionary;

    /// <summary>
    /// Gets the entries of the dictionary, creating it when the document has none.
    /// </summary>
    public PdfDictionaryElements Elements => GetOrCreate().Elements;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title
    {
        get => GetText("/Title");
        set => SetText("/Title", value);
    }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author
    {
        get => GetText("/Author");
        set => SetText("/Author", value);
    }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject
    {
        get => GetText("/Subject");
        set => SetText("/Subject", value);
    }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public string Keywords
    {
        get => GetText("/Keywords");
        set => SetText("/Keywords", value);
    }

    /// <summary>
    /// Gets or sets the application that created the original document.
    /// </summary>
    public string Creator
    {
        get => GetText("/Creator");
        set => SetText("/Creator", value);
    }

    /// <summary>
    /// Gets or sets the application that wrote the PDF.
    /// </summary>
    public string Producer
    {
        get => GetText("/Producer");
        set => SetText("/Producer", value);
    }

    /// <summary>
    /// Gets or sets when the document was created.
    /// </summary>
    public DateTimeOffset? CreationDate
    {
        get => Dictionary?.Elements.GetDateTime("/CreationDate");
        set => SetDate("/CreationDate", value);
    }

    /// <summary>
    /// Gets or sets when the document was last changed.
    /// </summary>
    public DateTimeOffset? ModificationDate
    {
        get => Dictionary?.Elements.GetDateTime("/ModDate");
        set => SetDate("/ModDate", value);
    }

    private PdfDictionary GetOrCreate()
    {
        if (Dictionary is { } existing)
        {
            return existing;
        }

        var created = new PdfDictionary(_document);

        _document.InfoReference = _document.AddObject(created);

        return created;
    }

    private string GetText(string key)
    {
        return Dictionary?.Elements.GetString(key) ?? string.Empty;
    }

    private void SetText(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Dictionary?.Elements.Remove(key);

            return;
        }

        GetOrCreate().Elements.SetString(key, value);
    }

    private void SetDate(string key, DateTimeOffset? value)
    {
        if (value is null)
        {
            Dictionary?.Elements.Remove(key);

            return;
        }

        GetOrCreate().Elements.SetDateTime(key, value.Value);
    }
}
