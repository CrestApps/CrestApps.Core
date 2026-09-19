namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// One item a connector found, and enough about it to tell whether it has changed.
/// </summary>
/// <param name="ItemId">What the connector knows the item by. Stable across runs.</param>
/// <param name="ChangeToken">An opaque value that changes when the item does. Compared verbatim, never parsed.</param>
/// <param name="SizeBytes">How large the item is, when the connector knows.</param>
/// <param name="LastModifiedUtc">When the item last changed, when the connector knows.</param>
public sealed record IngestionItemRef(
    string ItemId,
    string ChangeToken = null,
    long? SizeBytes = null,
    DateTimeOffset? LastModifiedUtc = null);
