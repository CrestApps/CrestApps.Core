namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Represents the format-agnostic content that an <see cref="IGeneratedFileWriter"/> turns into a
/// downloadable file. Writers use whichever parts of the content are relevant to their target format:
/// document-style writers (text, markdown, PDF, Word) render <see cref="Text"/>, while tabular writers
/// (CSV, spreadsheet) render <see cref="Header"/> and <see cref="Rows"/> when present.
/// </summary>
public sealed class GeneratedFileContent
{
    /// <summary>
    /// Gets or sets the optional document title used as a heading by document-style writers.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the free-form body content. May be plain text or Markdown depending on the target format.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the optional tabular header row. When set, tabular writers render structured data.
    /// </summary>
    public IReadOnlyList<string> Header { get; set; }

    /// <summary>
    /// Gets or sets the optional tabular data rows that accompany <see cref="Header"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; }

    /// <summary>
    /// Gets a value indicating whether the content carries a tabular header that writers can render.
    /// </summary>
    public bool HasTable => Header is { Count: > 0 };
}
