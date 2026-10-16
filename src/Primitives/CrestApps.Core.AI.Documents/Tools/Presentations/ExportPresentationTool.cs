using System.Globalization;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Hands the user the deck as a file: the PowerPoint file itself, or a PDF of its slides.
/// </summary>
internal sealed class ExportPresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ExportPresentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportPresentationTool"/> class.
    /// </summary>
    public ExportPresentationTool()
        : base(
            TheName,
            "Gives the user the presentation as a file to download: 'pptx' (default) is the PowerPoint file with every change made in this conversation, which opens in PowerPoint, Keynote and Google Slides; 'pdf' is a PDF with one page per slide, where the host supports it. Returns a download marker to repeat verbatim in your answer. Do not use generate_file for presentations.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "format": { "type": "string", "enum": ["pptx", "pdf"] },
                "file_name": { "type": "string", "description": "The file name, without or with its extension. Defaults to the presentation's name." },
                "slides": { "type": ["integer", "string", "array"], "description": "For pdf: only these slides." },
                "include_hidden": { "type": "boolean", "description": "For pdf: include hidden slides. Defaults to false." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Exports the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var format = call.Arguments.String("format", "type", "extension")?.Trim().TrimStart('.').ToLowerInvariant() ?? "pptx";
        var documents = call.Services.GetService<IGeneratedDocumentService>();

        if (documents is null)
        {
            return "Files cannot be created on this host.";
        }

        if (format is not ("pptx" or "powerpoint" or "pdf"))
        {
            throw new PresentationArgumentException($"\"{format}\" is not a format a presentation can be exported as. Use pptx or pdf; for the outline, notes or a script use export_presentation_content.");
        }

        var fileName = FileName(call.Arguments.String("file_name", "filename", "name") ?? deck.FileName ?? deck.Name, format == "pdf" ? ".pdf" : ".pptx");
        var isDefaultFile = format != "pdf" && string.Equals(fileName, DefaultFileName(deck), StringComparison.OrdinalIgnoreCase);

        // A preview in this turn already gave this revision of the deck as a download; a second copy of the same
        // file would only make the reader wonder which one to take.
        if (isDefaultFile && PreviewDownloads.TryGet(DownloadKey(deck), DownloadVersion(deck), out var offered))
        {
            return $"This revision of \"{deck.Name}\" was already given as the download \"{fileName}\" in this turn. Return this download marker verbatim and do not call export_presentation again for this file: {offered}";
        }

        byte[] bytes;
        var summary = string.Empty;

        if (format == "pdf")
        {
            var renderer = call.Services.GetService<IPresentationPdfRenderer>()
                ?? throw new PresentationArgumentException("PDF export is not available on this host; export as pptx instead.");

            var outline = await call.ReadAsync(deck, cancellationToken);
            var slides = call.Slides(outline, false);
            var includeHidden = call.Arguments.Bool("include_hidden") == true;
            var numbers = (slides.Count > 0 ? slides : Enumerable.Range(1, outline.Slides.Count))
                .Where(number => includeHidden || slides.Contains(number) || !outline.Slides[number - 1].Hidden)
                .ToList();

            if (numbers.Count == 0)
            {
                throw new PresentationArgumentException("There are no slides to export.");
            }

            var model = await call.Session.ReadAsync(deck, new PresentationReadOptions { IncludeImageData = true, Slides = numbers.ToHashSet() }, cancellationToken);
            var drawings = numbers.Select(number => SlideDrawingBuilder.Build(model, model.Slides[number - 1])).ToList();

            bytes = await renderer.RenderAsync(drawings, model.Title ?? deck.Name, cancellationToken);
            summary = $"a PDF of {PresentationDescriber.Count(numbers.Count, "slide")}";
        }
        else
        {
            bytes = await ReadPowerPointAsync(call, deck, cancellationToken);
            summary = "the PowerPoint file";
        }

        var result = await CreateAsync(call, documents, deck, fileName, bytes, cancellationToken);

        if (isDefaultFile)
        {
            PreviewDownloads.Record(DownloadKey(deck), DownloadVersion(deck), result.ReferenceToken);
        }

        return string.IsNullOrEmpty(result.ReferenceToken)
            ? $"Created \"{result.Document.FileName}\", {summary} of \"{deck.Name}\" ({(bytes.Length / 1024d).ToString("0", CultureInfo.InvariantCulture)} KB). The generated document id is {result.Document.ItemId}."
            : $"Created {summary} of \"{deck.Name}\". Return this download marker verbatim and do not call export_presentation again for this file: {result.ReferenceToken}";
    }

    /// <summary>
    /// Writes the deck as its PowerPoint file under the name it is downloaded under by default.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="deck">The deck.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The download marker, or <see langword="null"/> when this host cannot give one.</returns>
    internal static async Task<string> ExportPowerPointAsync(PresentationToolCall call, PresentationDeckState deck, CancellationToken cancellationToken)
    {
        var documents = call.Services.GetService<IGeneratedDocumentService>();

        if (documents is null)
        {
            return null;
        }

        var bytes = await ReadPowerPointAsync(call, deck, cancellationToken);
        var result = await CreateAsync(call, documents, deck, DefaultFileName(deck), bytes, cancellationToken);

        return result.ReferenceToken;
    }

    /// <summary>
    /// Gets the file name a deck is downloaded under when no other name or format is asked for.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <returns>The file name.</returns>
    internal static string DefaultFileName(PresentationDeckState deck)
    {
        return FileName(deck.FileName ?? deck.Name, ".pptx");
    }

    /// <summary>
    /// Gets what identifies a deck among the downloads given in a turn.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <returns>The key.</returns>
    internal static string DownloadKey(PresentationDeckState deck)
    {
        return "presentation|" + deck.Id;
    }

    /// <summary>
    /// Gets what identifies the revision of a deck that is downloaded.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <returns>The revision.</returns>
    internal static string DownloadVersion(PresentationDeckState deck)
    {
        return deck.Revision.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<byte[]> ReadPowerPointAsync(PresentationToolCall call, PresentationDeckState deck, CancellationToken cancellationToken)
    {
        var package = await call.Session.Workspace.ReadAsync(deck)
            ?? throw new PresentationArgumentException($"The file of the presentation \"{deck.Name}\" is missing from the workspace.");

        var issues = await call.Session.Engine.ValidateAsync(package, cancellationToken);

        if (issues.Count > 0 && call.Logger?.IsEnabled(LogLevel.Warning) == true)
        {
            call.Logger.LogWarning("The presentation '{DeckId}' has {IssueCount} schema issue(s) on export; the first is: {Issue}", deck.Id, issues.Count, issues[0].Description);
        }

        return package;
    }

    private static async Task<GeneratedDocumentResult> CreateAsync(PresentationToolCall call, IGeneratedDocumentService documents, PresentationDeckState deck, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        var result = await documents.CreateAsync(
            new GeneratedDocumentRequest(call.Session.ReferenceId, call.Session.ReferenceType, fileName, new GeneratedFileContent { Title = deck.Name, EncodedContent = bytes }),
            cancellationToken);

        if (call.Logger?.IsEnabled(LogLevel.Debug) == true)
        {
            call.Logger.LogDebug("Exported the presentation '{DeckId}' revision {Revision} as '{FileName}' ({ByteCount} bytes).", deck.Id, deck.Revision, fileName, bytes.Length);
        }

        return result;
    }

    private static string FileName(string name, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "presentation").Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        var stem = Path.GetExtension(cleaned) is ".pptx" or ".pdf" or ".potx" or ".ppt" ? Path.GetFileNameWithoutExtension(cleaned) : cleaned;

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "presentation";
        }

        return (stem.Length > 100 ? stem[..100] : stem) + extension;
    }
}
