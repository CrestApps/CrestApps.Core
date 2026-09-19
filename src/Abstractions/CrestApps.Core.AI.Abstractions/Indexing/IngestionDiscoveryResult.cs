namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// What one discovery pass found, and whether it found everything.
/// </summary>
/// <param name="Items">The items.</param>
/// <param name="IsComplete">
/// Whether the listing is the whole of what the source holds. A partial listing must never be used to decide
/// that anything was deleted.
/// </param>
/// <param name="Message">Why the listing was incomplete, when it was.</param>
/// <param name="DiscoveryCursor">
/// Where the next run should resume, when this listing was only a window onto the source. A result that
/// carries one is never complete, so nothing is ever removed on the strength of a window.
/// </param>
public sealed record IngestionDiscoveryResult(
    IReadOnlyList<IngestionItemRef> Items,
    bool IsComplete,
    string Message = null,
    string DiscoveryCursor = null);
