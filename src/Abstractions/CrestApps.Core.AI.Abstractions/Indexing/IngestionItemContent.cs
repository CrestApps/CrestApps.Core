namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// One fetched item, ready to be read.
/// </summary>
public sealed class IngestionItemContent : IAsyncDisposable
{
    /// <summary>
    /// Gets the content.
    /// </summary>
    public Stream Content { get; init; }

    /// <summary>
    /// Gets the media type the source declared, when it declared one.
    /// </summary>
    public string MediaType { get; init; }

    /// <summary>
    /// Gets the item's title, when the source has one.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the file name, which decides the reader when the media type says nothing.
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// Releases the content.
    /// </summary>
    /// <returns>A task that completes when the content is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Content is not null)
        {
            await Content.DisposeAsync();
        }
    }
}
