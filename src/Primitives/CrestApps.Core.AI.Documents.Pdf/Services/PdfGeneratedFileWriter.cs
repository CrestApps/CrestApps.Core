using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as a PDF document.
/// <para>
/// The content is described as a composed document and rendered by the same composer the PDF agent uses, so
/// <c>generate_file</c>, a tabular export and the agent all get the same page setup, typography, running
/// footer and table styling. Markdown and HTML become real headings, lists, quotes, code blocks, rules and
/// tables; a tabular export keeps its number formats, header colours, banding, highlighted cells, calculated
/// columns, total row and charts, so the PDF reads like the workbook and its preview.
/// </para>
/// </summary>
public sealed class PdfGeneratedFileWriter : IGeneratedFileWriter
{
    private readonly PdfDocumentComposer _composer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfGeneratedFileWriter"/> class with the default
    /// composition settings.
    /// </summary>
    public PdfGeneratedFileWriter()
        : this(Options.Create(new PdfCompositionOptions()), TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfGeneratedFileWriter"/> class.
    /// </summary>
    /// <param name="options">The composition defaults.</param>
    /// <param name="timeProvider">The time provider used for dates printed in the document.</param>
    public PdfGeneratedFileWriter(
        IOptions<PdfCompositionOptions> options,
        TimeProvider timeProvider)
    {
        _composer = new PdfDocumentComposer(options, timeProvider);
    }

    /// <summary>
    /// Writes the content as a PDF to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        var definition = PdfGeneratedContentMapper.ToDefinition(content);
        var result = await _composer.ComposeAsync(definition, images: null, cancellationToken);

        await destination.WriteAsync(result.Bytes, cancellationToken);
    }
}
