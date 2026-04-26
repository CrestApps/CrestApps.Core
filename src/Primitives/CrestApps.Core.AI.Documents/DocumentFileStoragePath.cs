namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Provides functionality for document File Storage Path.
/// </summary>
public static class DocumentFileStoragePath
{
    public static (string StoredFileName, string StoragePath) Create(string referenceType, string referenceId, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceType);
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var storagePath = Path.Combine("documents", referenceType, referenceId, storedFileName)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return (storedFileName, storagePath);
    }
}
