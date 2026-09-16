using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

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
    /// Gets or sets the optional presentation applied by spreadsheet writers: per-column number formats
    /// and storage types, header styling, conditional formatting, live formulas, a total row, and
    /// embedded charts. Writers that cannot express presentation (CSV, plain text) ignore it.
    /// </summary>
    public SpreadsheetFormatting SpreadsheetFormatting { get; set; }

    /// <summary>
    /// Gets or sets the worksheets to write, for formats that support more than one. When this is
    /// populated it replaces <see cref="Header"/> and <see cref="Rows"/>, and each sheet carries its own
    /// presentation.
    /// </summary>
    public IList<GeneratedSheet> Sheets { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether the content carries a tabular header that writers can render.
    /// </summary>
    public bool HasTable => Header is { Count: > 0 } || Sheets.Any(sheet => sheet is { HasTable: true });

    /// <summary>
    /// Returns the worksheets that make up this content. Multi-sheet content returns its
    /// <see cref="Sheets"/>; single-sheet content returns one sheet built from <see cref="Header"/>,
    /// <see cref="Rows"/>, and <see cref="SpreadsheetFormatting"/>, so every writer can treat the two
    /// shapes uniformly.
    /// </summary>
    /// <returns>The worksheets contained in the content.</returns>
    public IReadOnlyList<GeneratedSheet> GetSheets()
    {
        if (Sheets is { Count: > 0 })
        {
            return [.. Sheets];
        }

        return
        [
            new GeneratedSheet
            {
                Name = SpreadsheetFormatting?.SheetName,
                Header = Header ?? [],
                Rows = Rows ?? [],
                Formatting = SpreadsheetFormatting,
            },
        ];
    }
}
