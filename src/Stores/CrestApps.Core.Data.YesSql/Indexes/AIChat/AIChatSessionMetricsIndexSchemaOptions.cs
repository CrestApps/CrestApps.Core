namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// Configures the schema options used when creating the <c>AIChatSessionMetricsIndex</c>
/// table, including column lengths and whether to create named database indexes.
/// </summary>
public sealed class AIChatSessionMetricsIndexSchemaOptions
{
    /// <summary>
    /// Gets the YesSql collection name to target when creating the schema.
    /// </summary>
    public string CollectionName { get; init; }

    /// <summary>
    /// Gets the maximum column length for the <c>SessionId</c> column. Defaults to <c>44</c>.
    /// </summary>
    public int SessionIdLength { get; init; } = 44;

    /// <summary>
    /// Gets the maximum column length for the <c>ProfileId</c> column. Defaults to <c>26</c>.
    /// </summary>
    public int ProfileIdLength { get; init; } = 26;

    /// <summary>
    /// Gets the maximum column length for the <c>VisitorId</c> column. Defaults to <c>255</c>.
    /// </summary>
    public int VisitorIdLength { get; init; } = 255;

    /// <summary>
    /// Gets the maximum column length for the <c>UserId</c> column. Defaults to <c>255</c>.
    /// </summary>
    public int UserIdLength { get; init; } = 255;

    /// <summary>
    /// Gets a value indicating whether named database indexes are created on the metrics table.
    /// </summary>
    public bool CreateNamedIndexes { get; init; }
}
