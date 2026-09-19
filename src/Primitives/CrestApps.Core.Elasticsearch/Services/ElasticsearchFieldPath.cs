namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Stores a configured Elasticsearch field name and its reusable dotted-path segments.
/// </summary>
internal readonly struct ElasticsearchFieldPath
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchFieldPath" /> struct.
    /// </summary>
    /// <param name="fieldName">The configured field name.</param>
    internal ElasticsearchFieldPath(string fieldName)
    {
        OriginalName = fieldName;
        Segments = string.IsNullOrEmpty(fieldName) || !fieldName.Contains('.', StringComparison.Ordinal)
            ? null
            : fieldName.Split('.');
    }

    /// <summary>
    /// Gets the original configured field name.
    /// </summary>
    internal string OriginalName { get; }

    /// <summary>
    /// Gets the reusable dotted-path segments.
    /// </summary>
    internal string[] Segments { get; }
}
