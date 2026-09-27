using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// The Open XML implementation of <see cref="IPresentationEngine"/> and of the <c>.pptx</c> and <c>.potx</c>
/// <see cref="IPresentationImporter"/>.
/// </summary>
public sealed class OpenXmlPresentationEngine : IPresentationEngine, IPresentationImporter
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenXmlPresentationEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlPresentationEngine"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider, for the dates recorded in document properties.</param>
    /// <param name="logger">The logger.</param>
    public OpenXmlPresentationEngine(
        TimeProvider timeProvider,
        ILogger<OpenXmlPresentationEngine> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new deck.
    /// </summary>
    /// <param name="options">How to set the deck up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<byte[]> CreateAsync(PresentationCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (options.TemplatePackage is { Length: > 0 })
        {
            return Task.FromResult(PresentationPackageFactory.CreateFromTemplate(options.TemplatePackage, options.KeepTemplateSlides, options.Title, now));
        }

        return Task.FromResult(PresentationPackageFactory.Create(options, now));
    }

    /// <summary>
    /// Reads a deck into its resolved model.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="options">How much to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<PresentationModel> ReadAsync(byte[] package, PresentationReadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var stream = new MemoryStream(package, writable: false);
        using var document = PresentationDocument.Open(stream, false);

        return Task.FromResult(OpenXmlPresentationReader.Read(document, options));
    }

    /// <summary>
    /// Applies a batch of edits as a unit.
    /// </summary>
    /// <param name="package">The package to edit.</param>
    /// <param name="edits">The edits, applied in order.</param>
    /// <param name="context">What the engine needs to know about the conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<PresentationEditResult> EditAsync(
        byte[] package,
        IReadOnlyList<PresentationEdit> edits,
        PresentationEditContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(edits);

        using var stream = new MemoryStream();
        stream.Write(package);
        stream.Position = 0;

        var result = new PresentationEditResult();

        using (var document = PresentationDocument.Open(stream, true))
        {
            var editor = new OpenXmlPresentationEditor(document, context ?? new PresentationEditContext(), result);

            foreach (var edit in edits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    editor.Apply(edit);
                }
                catch (PresentationEditException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OpenXmlPackageException or FormatException or KeyNotFoundException)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(exception, "A presentation edit of type '{EditType}' failed.", edit.GetType().Name);
                    }

                    throw new PresentationEditException($"The change could not be made to this deck: {exception.Message}", exception);
                }
            }

            editor.Complete();
            document.PackageProperties.Modified = _timeProvider.GetUtcNow().UtcDateTime;
        }

        result.Package = stream.ToArray();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Validates a package against the Office file format schemas.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<PresentationValidationIssue>> ValidateAsync(byte[] package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var stream = new MemoryStream(package, writable: false);
        using var document = PresentationDocument.Open(stream, false);

        var directory = new OpenXmlSlideDirectory(document.PresentationPart);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var issues = new List<PresentationValidationIssue>();

        foreach (var error in validator.Validate(document, cancellationToken).Take(200))
        {
            var slideNumber = error.Part is SlidePart slidePart ? directory.Find(slidePart)?.Number : null;

            issues.Add(new PresentationValidationIssue
            {
                Description = error.Description,
                Part = error.Part?.Uri?.OriginalString,
                Path = error.Path?.XPath,
                SlideNumber = slideNumber,
            });
        }

        return Task.FromResult<IReadOnlyList<PresentationValidationIssue>>(issues);
    }

    /// <summary>
    /// Imports an uploaded deck or template as an editable deck.
    /// </summary>
    /// <param name="source">The uploaded file.</param>
    /// <param name="fileName">The uploaded file's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<byte[]> ImportAsync(Stream source, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        try
        {
            buffer.Position = 0;

            using (var document = PresentationDocument.Open(buffer, true))
            {
                if (document.PresentationPart?.Presentation is null)
                {
                    return null;
                }

                if (document.DocumentType != PresentationDocumentType.Presentation)
                {
                    // A template, slide show or macro-enabled deck becomes a plain deck: the copy is what the
                    // workspace edits and exports, and a macro the reader never asked for is not carried into
                    // a file this host hands back.
                    document.ChangeDocumentType(PresentationDocumentType.Presentation);
                }
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is OpenXmlPackageException or InvalidDataException or IOException or InvalidOperationException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(exception, "The uploaded file '{FileName}' could not be read as a presentation.", fileName);
            }

            return null;
        }
    }
}
