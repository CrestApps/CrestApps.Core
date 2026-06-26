namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Identifies file types that the tabular data tools can load into an in-memory database.
/// </summary>
internal static class TabularFileTypes
{
    private static readonly string[] _extensions = [".csv", ".tsv", ".xlsx", ".xls"];

    /// <summary>
    /// Determines whether the supplied file name represents a supported tabular file.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> when the file is tabular; otherwise <see langword="false"/>.</returns>
    public static bool IsTabular(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        foreach (var extension in _extensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
