using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Default <see cref="IGeneratedDocumentService"/> that writes generated content through the registered
/// <see cref="IGeneratedFileWriter"/>, stores it via <see cref="IDocumentFileStore"/>, persists the
/// metadata via <see cref="IAIDocumentStore"/>, and registers a download reference on the active
/// <see cref="AIInvocationScope"/>.
/// </summary>
public sealed class DefaultGeneratedDocumentService : IGeneratedDocumentService
{
    private readonly IGeneratedFileWriterResolver _writerResolver;
    private readonly IAIDocumentStore _documentStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultGeneratedDocumentService"/> class.
    /// </summary>
    /// <param name="writerResolver">The resolver used to locate the writer for the requested format.</param>
    /// <param name="documentStore">The document metadata store.</param>
    /// <param name="fileStore">The document file store.</param>
    /// <param name="timeProvider">The time provider.</param>
    public DefaultGeneratedDocumentService(
        IGeneratedFileWriterResolver writerResolver,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        TimeProvider timeProvider)
    {
        _writerResolver = writerResolver;
        _documentStore = documentStore;
        _fileStore = fileStore;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Writes the requested content to a new generated document and registers its download reference.
    /// </summary>
    /// <param name="request">The generation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<GeneratedDocumentResult> CreateAsync(GeneratedDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var extension = Path.GetExtension(request.FileName);

        if (!_writerResolver.TryResolve(extension, out var writer))
        {
            throw new NotSupportedException($"No generated file writer is registered for the '{extension}' format.");
        }

        await using var buffer = new MemoryStream();
        await writer.WriteAsync(request.Content, buffer, cancellationToken);
        buffer.Position = 0;

        var documentId = UniqueId.GenerateId();
        var (storedFileName, storagePath) = DocumentFileStoragePath.Create(
            request.ReferenceType,
            request.ReferenceId,
            request.FileName);

        await _fileStore.SaveFileAsync(storagePath, buffer);

        var document = new AIDocument
        {
            ItemId = documentId,
            ReferenceId = request.ReferenceId,
            ReferenceType = request.ReferenceType,
            FileName = request.FileName,
            StoredFileName = storedFileName,
            StoredFilePath = storagePath,
            ContentType = MediaTypeHelper.InferMediaType(extension),
            FileSize = buffer.Length,
            UploadedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        await _documentStore.CreateAsync(document, cancellationToken);

        var referenceToken = AddDownloadReference(document);

        return new GeneratedDocumentResult(document, referenceToken);
    }

    private static string AddDownloadReference(AIDocument document)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return null;
        }

        var referenceIndex = invocationContext.NextReferenceIndex();
        var template = $"[doc:{referenceIndex}]";
        invocationContext.ToolReferences.TryAdd(template, new AICompletionReference
        {
            Text = document.FileName,
            Title = document.FileName,
            Index = referenceIndex,
            ReferenceId = document.ItemId,
            ReferenceType = AIReferenceTypes.DataSource.Document,
            IsGenerated = true,
        });

        return template;
    }
}
