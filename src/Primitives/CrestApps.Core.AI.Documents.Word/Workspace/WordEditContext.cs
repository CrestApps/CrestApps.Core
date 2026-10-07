using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// What an editing tool works with while it changes a document: the open document, where it came from, the
/// design content is added with, and who and when the change is recorded as.
/// </summary>
internal sealed class WordEditContext
{
    private WordDesign _design;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordEditContext"/> class.
    /// </summary>
    /// <param name="package">The open document.</param>
    /// <param name="source">The document that was opened.</param>
    /// <param name="author">The name changes are signed with.</param>
    /// <param name="now">The time changes are recorded at.</param>
    public WordEditContext(WordPackage package, WordSource source, string author, DateTime now)
    {
        Package = package;
        Source = source;
        Author = author;
        Now = now;
    }

    /// <summary>
    /// Gets the open document.
    /// </summary>
    public WordPackage Package { get; }

    /// <summary>
    /// Gets the document that was opened.
    /// </summary>
    public WordSource Source { get; }

    /// <summary>
    /// Gets the name comments and tracked changes are signed with.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the time changes are recorded at.
    /// </summary>
    public DateTime Now { get; }

    /// <summary>
    /// Gets or sets the design content is added with: the one the document was created with, or one read from
    /// an uploaded document's own styles.
    /// </summary>
    public WordDesign Design
    {
        get => _design ??= Source?.Working?.Design?.Clone() ?? WordDesignReader.Infer(Package.MainPart);
        set
        {
            _design = value;
            DesignChanged = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the tool replaced the design, so the workspace remembers the new one.
    /// </summary>
    public bool DesignChanged { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool changed anything. A tool that finds nothing to do clears
    /// it, and no new version is saved.
    /// </summary>
    public bool Changed { get; set; } = true;

    /// <summary>
    /// Creates a block writer for the document's body, sized to the section an element is placed in.
    /// </summary>
    /// <param name="anchor">An element in the section, or <see langword="null"/> for the last section.</param>
    /// <returns>The writer.</returns>
    public WordBlockWriter CreateWriter(DocumentFormat.OpenXml.OpenXmlElement anchor = null)
    {
        return new WordBlockWriter(Package.MainPart, Design, WordSections.TextWidthTwips(WordSections.SectionOf(Package, anchor)));
    }
}
