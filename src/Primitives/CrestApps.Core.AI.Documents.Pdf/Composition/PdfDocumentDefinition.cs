namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Describes a PDF the way its author thinks about it — page setup, a look, running heads, a cover, a
/// table of contents and the content itself — rather than the way the file lays it out.
/// </summary>
/// <remarks>
/// This is the one description both the PDF agent and <see cref="Services.PdfGeneratedFileWriter"/> render
/// from, so a file the agent builds step by step and a file <c>generate_file</c> writes in one call are laid
/// out by the same code. It is stored in the conversation's PDF workspace between turns, which is what lets a
/// follow-up change one part of a document without restating the rest.
/// </remarks>
internal sealed class PdfDocumentDefinition
{
    /// <summary>
    /// Gets or sets the document title, written to the file's metadata and used by the <c>{title}</c> token.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the author written to the file's metadata and used by the <c>{author}</c> token.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the subject written to the file's metadata.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the keywords written to the file's metadata.
    /// </summary>
    public string Keywords { get; set; }

    /// <summary>
    /// Gets or sets the document language as a BCP 47 tag, for example <c>en-US</c>, which screen readers use
    /// to choose a voice.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the default page setup for every section that does not set its own.
    /// </summary>
    public PdfPageSetupDefinition PageSetup { get; set; }

    /// <summary>
    /// Gets or sets the colours, fonts and table style the document is drawn with.
    /// </summary>
    public PdfThemeDefinition Theme { get; set; }

    /// <summary>
    /// Gets or sets the running head printed at the top of every body page.
    /// </summary>
    public PdfHeaderFooterDefinition Header { get; set; }

    /// <summary>
    /// Gets or sets the running foot printed at the bottom of every body page.
    /// </summary>
    public PdfHeaderFooterDefinition Footer { get; set; }

    /// <summary>
    /// Gets or sets how pages are numbered.
    /// </summary>
    public PdfPageNumbersDefinition PageNumbers { get; set; }

    /// <summary>
    /// Gets or sets the cover page printed before everything else.
    /// </summary>
    public PdfCoverPageDefinition CoverPage { get; set; }

    /// <summary>
    /// Gets or sets the table of contents printed after the cover.
    /// </summary>
    public PdfTableOfContentsDefinition TableOfContents { get; set; }

    /// <summary>
    /// Gets or sets the watermark drawn across every page.
    /// </summary>
    public PdfWatermarkDefinition Watermark { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is written as PDF/A for long-term archiving.
    /// </summary>
    public bool? PdfA { get; set; }

    /// <summary>
    /// Gets or sets the sections, in order. A section starts on a new page and may change the page setup.
    /// </summary>
    public List<PdfSectionDefinition> Sections { get; set; } = [];

    /// <summary>
    /// Gets or sets the number the next block is identified by, so identifiers are never reused within a
    /// document even after blocks are removed.
    /// </summary>
    public int NextBlockNumber { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number the next section is identified by.
    /// </summary>
    public int NextSectionNumber { get; set; } = 1;
}
