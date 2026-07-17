using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Maps Elasticsearch JSON source documents to source documents.
/// </summary>
internal static class ElasticsearchSourceDocumentMapper
{
    /// <summary>
    /// Creates a reusable field path for repeated source-document mapping.
    /// </summary>
    /// <param name="fieldName">The configured field name.</param>
    /// <returns>The reusable field path.</returns>
    internal static ElasticsearchFieldPath CreateFieldPath(string fieldName)
    {
        return new ElasticsearchFieldPath(fieldName);
    }

    /// <summary>
    /// Extracts a source document from an Elasticsearch JSON source.
    /// </summary>
    /// <param name="source">The Elasticsearch JSON source.</param>
    /// <param name="titleFieldPath">The reusable title field path.</param>
    /// <param name="contentFieldPath">The reusable content field path.</param>
    /// <param name="treatWhitespaceAsEmpty">A value indicating whether whitespace-only values should use fallback content.</param>
    /// <returns>The extracted source document.</returns>
    internal static SourceDocument ExtractDocument(
        JsonObject source,
        ElasticsearchFieldPath titleFieldPath,
        ElasticsearchFieldPath contentFieldPath,
        bool treatWhitespaceAsEmpty)
    {
        string title = null;
        string content = null;

        if (IsConfigured(titleFieldPath.OriginalName, treatWhitespaceAsEmpty))
        {
            title = ResolveFieldValue(source, titleFieldPath).GetStringValue();
        }

        if (IsConfigured(contentFieldPath.OriginalName, treatWhitespaceAsEmpty))
        {
            content = ResolveFieldValue(source, contentFieldPath).GetStringValue();
        }

        if (IsMissing(content, treatWhitespaceAsEmpty))
        {
            content = source.ToJsonString();
        }

        if (IsMissing(title, treatWhitespaceAsEmpty) && !IsMissing(content, treatWhitespaceAsEmpty))
        {
            title = content.ExtractTitleFromContent();
        }

        var fields = new Dictionary<string, object>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var property in source)
        {
            fields[property.Key] = property.Value.GetRawValue();
        }

        return new SourceDocument
        {
            Title = title,
            Content = content,
            Fields = fields,
        };
    }

    /// <summary>
    /// Resolves a field value from a JSON object using a reusable dotted path.
    /// </summary>
    /// <param name="source">The Elasticsearch JSON source.</param>
    /// <param name="fieldPath">The reusable field path.</param>
    /// <returns>The resolved JSON node, or <see langword="null" /> when no value exists.</returns>
    internal static JsonNode ResolveFieldValue(JsonObject source, ElasticsearchFieldPath fieldPath)
    {
        if (source == null || string.IsNullOrEmpty(fieldPath.OriginalName))
        {
            return null;
        }

        if (source.TryGetPropertyValue(fieldPath.OriginalName, out var directNode))
        {
            return directNode;
        }

        if (fieldPath.Segments == null)
        {
            return null;
        }

        JsonNode current = source;
        foreach (var segment in fieldPath.Segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static bool IsConfigured(string value, bool treatWhitespaceAsEmpty)
    {
        return treatWhitespaceAsEmpty
            ? !string.IsNullOrWhiteSpace(value)
            : !string.IsNullOrEmpty(value);
    }

    private static bool IsMissing(string value, bool treatWhitespaceAsEmpty)
    {
        return treatWhitespaceAsEmpty
            ? string.IsNullOrWhiteSpace(value)
            : string.IsNullOrEmpty(value);
    }
}

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
