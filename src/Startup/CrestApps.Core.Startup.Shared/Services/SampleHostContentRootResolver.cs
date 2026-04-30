using System.Reflection;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Resolves the project content root for sample hosts so they can be launched
/// from the repository root with <c>dotnet run --project ...</c>.
/// </summary>
public static class SampleHostContentRootResolver
{
    private static readonly string[] SolutionMarkers = [".slnx", ".sln"];

    /// <summary>
    /// Resolves the content root for a sample host project.
    /// </summary>
    /// <param name="projectFileName">The sample host project file name.</param>
    /// <param name="baseDirectory">
    /// The base directory to walk upwards from. Defaults to <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    /// <param name="fallbackContentRoot">
    /// The content root to use when the project directory cannot be found.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    public static string ResolveContentRoot(
        string projectFileName,
        string baseDirectory = null,
        string fallbackContentRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectFileName);

        baseDirectory ??= AppContext.BaseDirectory;
        fallbackContentRoot ??= Directory.GetCurrentDirectory();

        // Strategy 1: Walk up from the binary output directory.
        var directory = TryFindProjectDirectory(baseDirectory, projectFileName);

        if (directory != null)
        {
            return directory;
        }

        // Strategy 2: Walk up from the current working directory.
        directory = TryFindProjectDirectory(fallbackContentRoot, projectFileName);

        if (directory != null)
        {
            return directory;
        }

        // Strategy 3: Walk up from the entry assembly location (may differ from AppContext.BaseDirectory
        // when the host is launched by an orchestrator such as Aspire DCP under Visual Studio).
        var assemblyDir = GetEntryAssemblyDirectory();

        if (assemblyDir != null && !string.Equals(assemblyDir, baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            directory = TryFindProjectDirectory(assemblyDir, projectFileName);

            if (directory != null)
            {
                return directory;
            }
        }

        // Strategy 4: Locate the solution/repository root by walking up from all known
        // starting paths, then search below the repo root for the project file.
        var solutionRoot = TryFindSolutionRoot(baseDirectory)
            ?? TryFindSolutionRoot(fallbackContentRoot)
            ?? TryFindSolutionRoot(assemblyDir);

        if (solutionRoot != null)
        {
            directory = TryFindProjectDirectoryBelow(solutionRoot, projectFileName);

            if (directory != null)
            {
                return directory;
            }
        }

        // Strategy 5: Search below the fallback directory as a last resort.
        directory = TryFindProjectDirectoryBelow(fallbackContentRoot, projectFileName);

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

    private static string TryFindSolutionRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startPath));

        while (current != null)
        {
            foreach (var marker in SolutionMarkers)
            {
                if (Directory.EnumerateFiles(current.FullName, "*" + marker, SearchOption.TopDirectoryOnly).Any())
                {
                    return current.FullName;
                }
            }

            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string TryFindProjectDirectoryBelow(string startPath, string projectFileName)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
        {
            return null;
        }

        try
        {
            var match = Directory.EnumerateFiles(startPath, projectFileName, SearchOption.AllDirectories)
                .FirstOrDefault();

            return match is null
                ? null
                : Path.GetDirectoryName(match);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string GetEntryAssemblyDirectory()
    {
        try
        {
            var location = Assembly.GetEntryAssembly()?.Location;

            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location);
            }
        }
        catch
        {
            // Ignore reflection failures in constrained environments.
        }

        return null;
    }
}

