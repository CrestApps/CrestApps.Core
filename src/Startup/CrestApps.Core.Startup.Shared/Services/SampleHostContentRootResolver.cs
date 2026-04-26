namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Resolves the project content root for sample hosts so they can be launched
/// from the repository root with <c>dotnet run --project ...</c>.
/// </summary>
public static class SampleHostContentRootResolver
{
    /// <summary>
    /// Resolves the content root for a sample host project.
    /// </summary>
    /// <param name="projectFileName">The sample host project file name.</param>
    /// <param name="baseDirectory">
    /// The base directory to walk upwards from. Defaults to <see cref="AppContext.BaseDirectory"/>.
    /// <param name="fallbackContentRoot">
    /// The content root to use when the project directory cannot be found.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// </param>
    public static string ResolveContentRoot(
        string projectFileName,
        string baseDirectory = null,
        string fallbackContentRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectFileName);

        baseDirectory ??= AppContext.BaseDirectory;
        fallbackContentRoot ??= Directory.GetCurrentDirectory();

        var directory = TryFindProjectDirectory(baseDirectory, projectFileName);

return directory ?? fallbackContentRoot;
    }

    private static string TryFindProjectDirectory(string startPath, string projectFileName)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startPath));

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, projectFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
