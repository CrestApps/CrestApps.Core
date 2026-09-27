namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A run of content that starts on a new page and may use its own page setup, for example a landscape
/// section holding a wide table.
/// </summary>
internal sealed class PdfSectionDefinition
{
    /// <summary>
    /// Gets or sets the identifier the section is addressed by, for example <c>s1</c>.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the page setup for this section, overriding the document's.
    /// </summary>
    public PdfPageSetupDefinition PageSetup { get; set; }

    /// <summary>
    /// Gets or sets the blocks, in reading order.
    /// </summary>
    public List<PdfBlockDefinition> Blocks { get; set; } = [];
}
