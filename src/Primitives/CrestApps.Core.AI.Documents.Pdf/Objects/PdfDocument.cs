using System.Globalization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigParsingOptions = UglyToad.PdfPig.ParsingOptions;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF file as objects that can be read, changed and saved: the package's own model, read through PdfPig.
/// </summary>
/// <remarks>
/// PdfPig parses the file, repairs what it can and decrypts it; the objects are converted the first time
/// they are asked for. Saving writes every object the document still uses into a new file, with the
/// page tree flattened into one list of pages.
/// </remarks>
internal sealed class PdfDocument : IDisposable
{
    private readonly Dictionary<(long Number, int Generation), PdfReference> _references = [];
    private readonly List<PdfReference> _order = [];
    private readonly List<Action> _saving = [];
    private readonly PigDocument _source;
    private PdfDocumentInformation _info;
    private int _nextObjectNumber = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocument"/> class: an empty document without pages.
    /// </summary>
    public PdfDocument()
    {
        Catalog = new PdfDictionary(this);
        Catalog.Elements.SetName("/Type", "/Catalog");
        AddObject(Catalog);
        Pages = new PdfPages(this);
        Version = "1.7";
    }

    private PdfDocument(PigDocument source)
    {
        _source = source;

        foreach (var reference in source.Structure.CrossReferenceTable.ObjectOffsets.Keys.OrderBy(key => key.ObjectNumber))
        {
            GetReference(reference);
        }

        var trailer = source.Structure.Trailer;

        Catalog = GetReference(trailer.Root).Value as PdfDictionary ?? new PdfDictionary(this);

        if (trailer.Info is IndirectReferenceToken info && GetReference(info.Data).Value is PdfDictionary)
        {
            InfoReference = GetReference(info.Data);
        }
        else if (PdfObjectModel.FromToken(trailer.Info, this, GetReference) is PdfDictionary direct)
        {
            InfoReference = AddObject(direct);
        }

        if (trailer.Identifier is { Count: > 0 } identifier)
        {
            FileIdentifier = [.. identifier.Select(Bytes).Where(bytes => bytes is not null)];
        }

        IsEncrypted = source.IsEncrypted;
        Version = source.Version.ToString("0.0", CultureInfo.InvariantCulture);
        Pages = new PdfPages(this);
        Pages.LoadTree(Catalog.Elements["/Pages"]);
    }

    /// <summary>
    /// Gets the document catalog: the root of the file.
    /// </summary>
    public PdfDictionary Catalog { get; }

    /// <summary>
    /// Gets the document information: title, author and the rest.
    /// </summary>
    public PdfDocumentInformation Info => _info ??= new PdfDocumentInformation(this);

    /// <summary>
    /// Gets the pages.
    /// </summary>
    public PdfPages Pages { get; }

    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public int PageCount => Pages.Count;

    /// <summary>
    /// Gets or sets the PDF version the header names, such as <c>1.7</c>.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets a value indicating whether the file was encrypted when it was read. A saved document is not
    /// encrypted.
    /// </summary>
    public bool IsEncrypted { get; }

    /// <summary>
    /// Gets or sets the natural language of the document, such as <c>en-US</c>, stored as the catalog's
    /// <c>/Lang</c>.
    /// </summary>
    public string Language
    {
        get => Catalog.Elements.GetString("/Lang");
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Catalog.Elements.Remove("/Lang");

                return;
            }

            Catalog.Elements.SetString("/Lang", value);
        }
    }

    /// <summary>
    /// Gets the file identifier the file was read with, or <see langword="null"/> when it had none.
    /// </summary>
    public IReadOnlyList<byte[]> FileIdentifier { get; }

    /// <summary>
    /// Gets or sets the reference to the document information dictionary, or <see langword="null"/>.
    /// </summary>
    internal PdfReference InfoReference { get; set; }

    /// <summary>
    /// Opens a PDF file.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password, when the file needs one to open.</param>
    /// <returns>The document. The caller disposes it.</returns>
    public static PdfDocument Open(byte[] bytes, string password = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var options = new PigParsingOptions
        {
            SkipMissingFonts = true,
        };

        if (!string.IsNullOrEmpty(password))
        {
            options.Password = password;
        }

        var source = PigDocument.Open(bytes, options);

        try
        {
            return new PdfDocument(source);
        }
        catch
        {
            source.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Stores an object on its own in the document, so other objects can refer to it.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>Its reference.</returns>
    public PdfReference AddObject(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Reference is not null)
        {
            return value.Reference;
        }

        while (_references.ContainsKey((_nextObjectNumber, 0)))
        {
            _nextObjectNumber++;
        }

        var reference = new PdfReference(_nextObjectNumber, value);

        _references[(_nextObjectNumber, 0)] = reference;
        _order.Add(reference);
        _nextObjectNumber++;
        value.Owner = this;
        value.Reference = reference;

        return reference;
    }

    /// <summary>
    /// Lists every array and dictionary stored on its own in the document, whether or not anything still
    /// refers to it. The cross-reference and object streams a file is packed in are not part of the content,
    /// and are not listed.
    /// </summary>
    /// <returns>The objects.</returns>
    public List<PdfObject> GetAllObjects()
    {
        return [.. _order
            .Select(reference => reference.Value)
            .OfType<PdfObject>()
            .Where(value => value is not PdfDictionary dictionary || dictionary.Elements.GetName("/Type") is not ("/XRef" or "/ObjStm"))];
    }

    /// <summary>
    /// Adds an empty A4 page at the end.
    /// </summary>
    /// <returns>The page.</returns>
    public PdfPage AddPage()
    {
        var page = new PdfPage(this);

        Pages.Add(page);

        return page;
    }

    /// <summary>
    /// Saves the document to bytes.
    /// </summary>
    /// <returns>The file.</returns>
    public byte[] Save()
    {
        return PdfObjectWriter.Write(this);
    }

    /// <summary>
    /// Saves the document to a stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Write(Save());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _source?.Dispose();
    }

    /// <summary>
    /// Registers work that completes the document before every save, such as embedding the fonts it writes with.
    /// </summary>
    /// <param name="action">The work.</param>
    internal void OnSaving(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _saving.Add(action);
    }

    /// <summary>
    /// Runs the work registered with <see cref="OnSaving"/>.
    /// </summary>
    internal void CompleteForSaving()
    {
        foreach (var action in _saving)
        {
            action();
        }
    }

    /// <summary>
    /// Returns the document's reference for an object of the file it was read from.
    /// </summary>
    /// <param name="reference">The object number and generation.</param>
    /// <returns>The reference.</returns>
    internal PdfReference GetReference(IndirectReference reference)
    {
        var key = (reference.ObjectNumber, reference.Generation);

        if (_references.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new PdfReference((int)Math.Min(reference.ObjectNumber, int.MaxValue), reference.Generation, Load);

        _references[key] = created;
        _order.Add(created);
        _nextObjectNumber = Math.Max(_nextObjectNumber, created.ObjectNumber + 1);

        return created;
    }

    private PdfItem Load(PdfReference reference)
    {
        if (_source is null)
        {
            return PdfNull.Value;
        }

        IToken token;

        try
        {
            token = _source.Structure.GetObject(new IndirectReference(reference.ObjectNumber, reference.Generation))?.Data;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An object PdfPig cannot read is missing, the way a viewer treats it.
            return PdfNull.Value;
        }

        var item = PdfObjectModel.FromToken(token, this, GetReference);

        if (item is PdfObject value)
        {
            value.Reference = reference;
        }

        return item;
    }

    private static byte[] Bytes(IToken token)
    {
        return token switch
        {
            HexToken hex => [.. hex.Bytes],
            StringToken text => text.GetBytes(),
            _ => null,
        };
    }
}
