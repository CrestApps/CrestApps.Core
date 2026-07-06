namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Provides functionality for document File Storage Path.
/// </summary>
public static class DocumentFileStoragePath
{
    /// <summary>
    /// Creates a relative storage path for a persisted document file.
    /// </summary>
    /// <param name="referenceType">The owning reference type.</param>
    /// <param name="referenceId">The owning reference identifier.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="subfolder">An optional storage subfolder beneath the owning reference.</param>
    /// <returns>The stored file name and normalized relative storage path.</returns>
    public static (string StoredFileName, string StoragePath) Create(
        string referenceType,
        string referenceId,
        string fileName,
        string subfolder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceType);
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var segments = new List<string>
        {
            "documents",
            referenceType,
            referenceId,
        };

        AppendSafeSegments(segments, subfolder);
        segments.Add(storedFileName);

        var storagePath = Path.Combine([.. segments])
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return (storedFileName, storagePath);
    }

    private static void AppendSafeSegments(List<string> segments, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
        {
            return;
        }

        foreach (var segment in subfolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException("The storage subfolder contains an invalid path.", nameof(subfolder));
            }

            segments.Add(segment);
        }
    }
}
