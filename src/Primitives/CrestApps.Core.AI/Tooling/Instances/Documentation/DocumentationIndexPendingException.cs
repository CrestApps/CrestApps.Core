namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// Thrown by a <see cref="CachingDocumentationSource"/> when a search arrives before the source's corpus
/// has finished building and the build did not complete within the allotted wait budget. The corpus keeps
/// building in the background, so a later search succeeds; the documentation search tool translates this
/// into a short "still indexing, try again" message for the model rather than an error, so a slow first
/// crawl does not turn into a retry loop.
/// </summary>
public sealed class DocumentationIndexPendingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationIndexPendingException"/> class.
    /// </summary>
    /// <param name="sourceName">The logical name of the source whose corpus is still being built.</param>
    public DocumentationIndexPendingException(string sourceName)
        : base($"The documentation source '{sourceName}' is still building its index.")
    {
        SourceName = sourceName;
    }

    /// <summary>
    /// Gets the logical name of the source whose corpus is still being built.
    /// </summary>
    public string SourceName { get; }
}
