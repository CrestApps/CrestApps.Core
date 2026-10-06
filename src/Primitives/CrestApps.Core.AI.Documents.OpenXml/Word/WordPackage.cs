using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// An open, editable word-processing document held in memory: created new with a style sheet, or opened
/// from the bytes of an existing file, changed, then saved back to bytes.
/// </summary>
/// <remarks>
/// The bytes a document is opened from are copied, so the caller's buffer — an upload, a stored working
/// copy — is never written to. A template or a macro-enabled document is opened as a plain document, because
/// what is saved is always a <c>.docx</c> file.
/// </remarks>
internal sealed class WordPackage : IDisposable
{
    /// <summary>
    /// The Word 2010 namespace paragraph identifiers are written in.
    /// </summary>
    public const string Word2010Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";

    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private readonly MemoryStream _stream;
    private WordParagraphIds _ids;
    private bool _closed;

    private WordPackage(MemoryStream stream, WordprocessingDocument document)
    {
        _stream = stream;
        Document = document;
    }

    /// <summary>
    /// Gets the open document.
    /// </summary>
    public WordprocessingDocument Document { get; }

    /// <summary>
    /// Gets the main document part.
    /// </summary>
    public MainDocumentPart MainPart => Document.MainDocumentPart;

    /// <summary>
    /// Gets the document body.
    /// </summary>
    public Body Body => MainPart.Document.Body;

    /// <summary>
    /// Gets the paragraph identifiers, giving every paragraph and row one the first time they are asked for.
    /// </summary>
    public WordParagraphIds Ids => _ids ??= WordParagraphIds.Ensure(MainPart);

    /// <summary>
    /// Creates a new, empty document with the style sheet of a design and a single section.
    /// </summary>
    /// <param name="design">The design.</param>
    /// <param name="section">The section properties, or <see langword="null"/> for a letter page with one-inch margins.</param>
    /// <returns>The document.</returns>
    public static WordPackage Create(WordDesign design, SectionProperties section = null)
    {
        ArgumentNullException.ThrowIfNull(design);

        var stream = new MemoryStream();
        var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();

        mainPart.Document = new Document(new Body(section ?? WordPageSizes.CreateSection("letter", landscape: false, marginPoints: 72)));

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = WordStyleSheet.Create(design);

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(
            new DefaultTabStop { Val = 720 },
            new CharacterSpacingControl { Val = CharacterSpacingValues.DoNotCompress },
            new Compatibility(new CompatibilitySetting
            {
                Name = CompatSettingNameValues.CompatibilityMode,
                Uri = "http://schemas.microsoft.com/office/word",
                Val = "15",
            }));

        var package = new WordPackage(stream, document);

        EnsureNamespaces(mainPart.Document);

        return package;
    }

    /// <summary>
    /// Opens an editable copy of a document.
    /// </summary>
    /// <param name="bytes">The document file.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a word-processing document.</exception>
    public static WordPackage Open(ReadOnlySpan<byte> bytes)
    {
        var stream = new MemoryStream(bytes.Length + 4096);

        stream.Write(bytes);
        stream.Position = 0;

        WordprocessingDocument document;

        try
        {
            document = WordprocessingDocument.Open(stream, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FileFormatException)
        {
            stream.Dispose();

            throw new InvalidDataException("The file is not a Word document that can be opened: " + ex.Message, ex);
        }

        if (document.MainDocumentPart?.Document?.Body is null)
        {
            document.Dispose();
            stream.Dispose();

            throw new InvalidDataException("The file is not a Word document: it has no document body.");
        }

        if (document.DocumentType != WordprocessingDocumentType.Document)
        {
            document.ChangeDocumentType(WordprocessingDocumentType.Document);
        }

        var package = new WordPackage(stream, document);

        EnsureNamespaces(document.MainDocumentPart.Document);

        return package;
    }

    /// <summary>
    /// Returns whether a file is a word-processing document this class opens.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <returns><see langword="true"/> for <c>.docx</c>, <c>.docm</c>, <c>.dotx</c> and <c>.dotm</c> files.</returns>
    public static bool IsWordFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        return string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".docm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dotx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dotm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Declares the namespaces paragraph identifiers are written in on a part's root element, so they are
    /// declared once rather than on every paragraph.
    /// </summary>
    /// <param name="root">The root element of a document, header, footer, notes or comments part.</param>
    public static void EnsureNamespaces(OpenXmlPartRootElement root)
    {
        if (root is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(root.LookupNamespace("w14")))
        {
            root.AddNamespaceDeclaration("w14", Word2010Namespace);
        }

        if (string.IsNullOrEmpty(root.LookupNamespace("mc")))
        {
            root.AddNamespaceDeclaration("mc", MarkupCompatibilityNamespace);
        }

        // An older reader is told it may skip the Word 2010 attributes rather than reject the file.
        var ignorable = root.MCAttributes?.Ignorable?.Value ?? string.Empty;

        if (!ignorable.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("w14", StringComparer.Ordinal))
        {
            root.MCAttributes ??= new MarkupCompatibilityAttributes();
            root.MCAttributes.Ignorable = string.IsNullOrWhiteSpace(ignorable) ? "w14" : ignorable.Trim() + " w14";
        }
    }

    /// <summary>
    /// Returns the document settings, creating the part when the document has none.
    /// </summary>
    /// <returns>The settings.</returns>
    public Settings GetOrCreateSettings()
    {
        var part = MainPart.DocumentSettingsPart ?? MainPart.AddNewPart<DocumentSettingsPart>();

        part.Settings ??= new Settings();

        return part.Settings;
    }

    /// <summary>
    /// Saves the document and returns its file. The package is closed afterwards.
    /// </summary>
    /// <returns>The <c>.docx</c> file.</returns>
    public byte[] Save()
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        Document.Dispose();
        _closed = true;

        return _stream.ToArray();
    }

    /// <summary>
    /// Closes the document without saving it.
    /// </summary>
    public void Dispose()
    {
        if (!_closed)
        {
            _closed = true;

            try
            {
                Document.Dispose();
            }
            catch (Exception ex) when (ex is OpenXmlPackageException or IOException or InvalidOperationException)
            {
                // A document closed without being saved is being discarded; failing to flush it changes nothing.
            }
        }

        _stream.Dispose();
    }
}
