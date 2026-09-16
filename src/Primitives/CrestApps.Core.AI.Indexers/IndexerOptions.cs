namespace CrestApps.Core.AI.Indexers;

/// <summary>
/// Host-wide limits on what an indexer may reach and how hard it may work.
/// </summary>
/// <remarks>
/// An indexer is configured by an administrator and runs unattended. These are the boundaries the host sets
/// around that: which folders may be read at all, how much one run may take on, and how many things it may
/// do at once.
/// </remarks>
public sealed class IndexerOptions
{
    /// <summary>
    /// Gets the folders a local-folder indexer may read from. A root outside every entry is refused.
    /// </summary>
    /// <remarks>
    /// The path is configured by an administrator through the admin UI, so without this an indexer is a way
    /// to read any file the host process can open. Empty means no local folder may be indexed at all, which
    /// is the safe default for a host that has not thought about it.
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
    /// Gets or sets how often, in minutes, the background service wakes to check whether an indexer is due.
    /// </summary>
    public int RunCheckIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the default interval, in minutes, between runs of an indexer that does not set its own.
    /// </summary>
    public int DefaultRunIntervalMinutes { get; set; } = (int)(24 * 60);

    /// <summary>
    /// Determines whether the supplied folder is one an indexer may read.
    /// </summary>
    /// <param name="rootPath">The folder.</param>
    /// <returns><see langword="true"/> when the folder sits inside an allowed root.</returns>
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
