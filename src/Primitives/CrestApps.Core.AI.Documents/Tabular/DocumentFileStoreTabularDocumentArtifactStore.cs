using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class DocumentFileStoreTabularDocumentArtifactStore : ITabularDocumentArtifactStore
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDocumentFileStore _fileStore;

    public DocumentFileStoreTabularDocumentArtifactStore(IDocumentFileStore fileStore)
    {
        _fileStore = fileStore;
    }

    public async Task<TabularDocumentArtifact> GetAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        await using var stored = await _fileStore.GetFileAsync(GetArtifactPath(documentId));

        if (stored is null)
        {
            return null;
        }

        await using var gzip = new GZipStream(stored, CompressionMode.Decompress);

        return await JsonSerializer.DeserializeAsync<TabularDocumentArtifact>(
            gzip,
            _jsonSerializerOptions,
            cancellationToken);
    }

    public async Task SaveAsync(
        string documentId,
        TabularDocumentArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentNullException.ThrowIfNull(artifact);

        await using var buffer = new MemoryStream();

        await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(gzip, artifact, _jsonSerializerOptions, cancellationToken);
        }

        buffer.Position = 0;
        await _fileStore.SaveFileAsync(GetArtifactPath(documentId), buffer);
    }

    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        cancellationToken.ThrowIfCancellationRequested();

        await _fileStore.DeleteFileAsync(GetArtifactPath(documentId));
    }

    private static string GetArtifactPath(string documentId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(documentId));
        var key = Convert.ToHexString(bytes).ToLowerInvariant();

        return $"documents/tabular-artifacts/{key}.json.gz";
    }
}
