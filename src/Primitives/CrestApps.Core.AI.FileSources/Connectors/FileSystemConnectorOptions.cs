namespace CrestApps.Core.AI.FileSources.Connectors;

/// <summary>
/// Where the file-system connector is allowed to read.
/// </summary>
/// <remarks>
/// The connector is configured by an administrator through the admin UI and then runs unattended, so where
/// it may look is a host decision rather than theirs: without a boundary set here, the file source screen is
/// a way to read any file the host process can open.
/// <para>
/// The boundary is the whole of what a host decides. Which folder inside it to read, what to match and
/// whether to recurse belong to the file source, which cannot widen the boundary whatever it asks for.
/// </para>
/// </remarks>
public sealed class FileSystemConnectorOptions
{
    /// <summary>
    /// Gets the folders the connector may read from. A folder outside every entry is refused.
    /// </summary>
    /// <remarks>
    /// An entry may be absolute, or relative to <see cref="BasePath"/> — <c>App_Data/file-sources</c> is the
    /// usual shape, which keeps everything readable inside one folder the host owns. Empty means no folder
    /// may be read at all, which is the safe default for a host that has not thought about it.
    /// <para>
    /// A single entry is also the folder a file source that names none reads, so the usual host -- one
    /// allowed root -- gets a working file source out of a name and a data source alone.
    /// </para>
    /// </remarks>
    public IList<string> AllowedRoots { get; } = [];

    /// <summary>
    /// Gets or sets the folder that relative paths — in <see cref="AllowedRoots"/> and in a file source's
    /// own settings — are resolved against. The host sets this to its content root.
    /// </summary>
    /// <remarks>
    /// Resolution never uses the process's current directory, which is not the content root under IIS, a
    /// Windows service, or <c>dotnet run</c> from another folder, and would quietly point an allowed root
    /// somewhere nobody intended.
    /// </remarks>
    public string BasePath { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Resolves a configured folder to a full path, if the host allows reading it.
    /// </summary>
    /// <param name="rootPath">The folder a file source was configured with, or blank for the allowed root.</param>
    /// <param name="resolved">The full path, when it is allowed.</param>
    /// <param name="reason">Why it was refused, when it was.</param>
    /// <returns><see langword="true"/> when the folder sits inside an allowed root.</returns>
    /// <remarks>
    /// A path containing a <c>..</c> segment is refused outright rather than normalized and then checked.
    /// Containment alone would accept <c>App_Data/file-sources/../../secrets</c> when it happens to land
    /// back inside a root, and a path that climbs out and back in is never what someone meant to type.
    /// </remarks>
    public bool TryResolveRoot(string rootPath, out string resolved, out string reason)
    {
        resolved = null;
        reason = null;

        // Naming a folder is optional. A file source that names none reads the folder the host allows it to
        // read, which is the one obvious default: refusing it made the simplest configuration anyone could
        // arrive at -- leave the box empty -- the one that silently ingested nothing.
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return TryResolveDefaultRoot(out resolved, out reason);
        }

        if (HasParentTraversal(rootPath))
        {
            reason = "A folder path may not contain '..'.";

            return false;
        }

        if (AllowedRoots.Count == 0)
        {
            reason = "This application is not configured to read any folder. Ask an administrator to add one to the allowed roots.";

            return false;
        }

        string full;

        try
        {
            full = Resolve(rootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "That folder is not a valid path.";

            return false;
        }

        foreach (var allowed in AllowedRoots)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            string allowedFull;

            try
            {
                allowedFull = Resolve(allowed);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (Contains(allowedFull, full))
            {
                resolved = full;

                return true;
            }
        }

        reason = "That folder is not one this application is configured to read. Ask an administrator to add it to the allowed roots.";

        return false;
    }

    /// <summary>
    /// Determines whether the supplied folder is one the connector may read.
    /// </summary>
    /// <param name="rootPath">The folder.</param>
    /// <returns><see langword="true"/> when the folder sits inside an allowed root.</returns>
    public bool IsAllowedRoot(string rootPath)
    {
        return TryResolveRoot(rootPath, out _, out _);
    }

    /// <summary>
    /// Resolves the folder a file source that named none reads.
    /// </summary>
    /// <param name="resolved">The full path, when there is one to default to.</param>
    /// <param name="reason">Why there is not, when there is not.</param>
    /// <returns><see langword="true"/> when a default folder could be decided.</returns>
    /// <remarks>
    /// Only a single allowed root is a default. With several there is no "the" folder, and picking one of
    /// them -- the first, say -- would quietly read files nobody pointed the source at.
    /// </remarks>
    private bool TryResolveDefaultRoot(out string resolved, out string reason)
    {
        resolved = null;
        reason = null;

        string only = null;

        foreach (var allowed in AllowedRoots)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            if (only is not null)
            {
                reason = "No folder is named, and this application is configured to read more than one folder, so there is no single one to fall back to. Name the folder to read.";

                return false;
            }

            only = allowed;
        }

        if (only is null)
        {
            reason = "This application is not configured to read any folder. Ask an administrator to add one to the allowed roots.";

            return false;
        }

        try
        {
            resolved = Resolve(only);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "The folder this application is configured to read is not a valid path.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Turns a configured path into a full path, resolving a relative one against <see cref="BasePath"/>.
    /// </summary>
    /// <param name="path">The configured path.</param>
    /// <returns>The full path.</returns>
    private string Resolve(string path)
    {
        var basePath = string.IsNullOrWhiteSpace(BasePath) ? AppContext.BaseDirectory : BasePath;

        return Path.GetFullPath(path, Path.GetFullPath(basePath));
    }

    /// <summary>
    /// Determines whether one full path sits inside another.
    /// </summary>
    /// <param name="root">The containing folder, as a full path.</param>
    /// <param name="candidate">The folder being checked, as a full path.</param>
    /// <returns><see langword="true"/> when the candidate is the root or sits beneath it.</returns>
    /// <remarks>
    /// Compared as a path, not as a string, in both directions: <c>C:\data</c> must not admit
    /// <c>C:\database</c>, and a folder legitimately named <c>..archive</c> must not be read as an escape
    /// because its relative path happens to start with two dots.
    /// </remarks>
    internal static bool Contains(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);

        if (relative == "." || relative.Length == 0)
        {
            return true;
        }

        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        var first = relative.Split(['/', '\\'], 2)[0];

        return first != "..";
    }

    /// <summary>
    /// Determines whether a path contains a <c>..</c> segment.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> when any segment is exactly <c>..</c>.</returns>
    /// <remarks>
    /// Segments are compared whole, so a folder legitimately named <c>..archive</c> is not refused.
    /// </remarks>
    internal static bool HasParentTraversal(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var segment in path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Trim() == "..")
            {
                return true;
            }
        }

        return false;
    }
}
