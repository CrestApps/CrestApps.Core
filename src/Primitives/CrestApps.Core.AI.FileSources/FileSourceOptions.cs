namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Host-wide limits on what a file source may reach and how hard it may work.
/// </summary>
/// <remarks>
/// A file source is configured by an administrator and runs unattended. These are the boundaries the host sets
/// around that: which folders may be read at all, how much one run may take on, and how many things it may
/// do at once.
/// </remarks>
public sealed class FileSourceOptions
{
    /// <summary>
    /// Gets the folders the file-system connector may read from.
    /// </summary>
    /// <remarks>
    /// Superseded by <c>FileSystemConnectorOptions.AllowedRoots</c>, which is where the connector's own
    /// settings live and which also takes paths relative to the content root. Entries set here are still
    /// carried over into it, because this key lives in places this repository cannot see — user secrets,
    /// environment variables, a deployed <c>appsettings.json</c> — and dropping it would take a host's
    /// allowed roots away silently.
    /// </remarks>
    public IList<string> AllowedLocalRoots { get; } = [];

    /// <summary>
    /// Gets or sets the most items one run may ingest. The rest are picked up by the next run.
    /// </summary>
    /// <remarks>
    /// A first sync of a large source is the expensive one. Capping it makes the cost predictable and lets
    /// an operator see results before committing to the whole corpus.
    /// </remarks>
    public int MaxItemsPerRun { get; set; } = 200;

    /// <summary>
    /// Gets or sets how many items one run fetches at a time.
    /// </summary>
    public int MaxConcurrentFetches { get; set; } = 2;

    /// <summary>
    /// Gets or sets how often, in minutes, the background service wakes to check whether a source is due.
    /// </summary>
    public int RunCheckIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the default interval, in minutes, between runs of a file source that does not set its own.
    /// </summary>
    public int DefaultRunIntervalMinutes { get; set; } = (int)(24 * 60);

    /// <summary>
    /// Determines whether the supplied folder is one a file source may read.
    /// </summary>
    /// <param name="rootPath">The folder.</param>
    /// <returns><see langword="true"/> when the folder sits inside an allowed root.</returns>
    /// <remarks>
    /// Superseded by <c>FileSystemConnectorOptions.TryResolveRoot</c>, which resolves relative paths against
    /// the content root, refuses a <c>..</c> outright, and says why it refused.
    /// </remarks>
    public bool IsAllowedLocalRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || AllowedLocalRoots.Count == 0)
        {
            return false;
        }

        string full;

        try
        {
            full = Path.GetFullPath(rootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        foreach (var allowed in AllowedLocalRoots)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            string allowedFull;

            try
            {
                allowedFull = Path.GetFullPath(allowed);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            // Compared as a path, not as a string: "C:\data" must not admit "C:\database".
            var relative = Path.GetRelativePath(allowedFull, full);

            if (relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)))
            {
                return true;
            }
        }

        return false;
    }
}
