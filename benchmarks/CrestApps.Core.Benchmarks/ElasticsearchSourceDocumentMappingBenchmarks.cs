using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares legacy per-document Elasticsearch source mapping with reusable field-path mapping.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class ElasticsearchSourceDocumentMappingBenchmarks
{
    private JsonObject[] _sources;
    private ElasticsearchFieldPath _keyFieldPath;
    private ElasticsearchFieldPath _titleFieldPath;
    private ElasticsearchFieldPath _contentFieldPath;
    private const string KeyFieldName = "metadata.identity.key";
    private const string TitleFieldName = "content.title";
    private const string ContentFieldName = "content.body";

    /// <summary>
    /// Gets or sets the representative field mapping shape.
    /// </summary>
    [Params("Nested", "DirectDotted")]
    public string Scenario { get; set; }

    /// <summary>
    /// Gets or sets the number of documents mapped in one source batch.
    /// </summary>
    [Params(1000, 10000)]
    public int DocumentCount { get; set; }

    /// <summary>
    /// Creates the selected source batch and verifies exact legacy/current output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _keyFieldPath = ElasticsearchSourceDocumentMapper.CreateFieldPath(KeyFieldName);
        _titleFieldPath = ElasticsearchSourceDocumentMapper.CreateFieldPath(TitleFieldName);
        _contentFieldPath = ElasticsearchSourceDocumentMapper.CreateFieldPath(ContentFieldName);
        _sources = new JsonObject[DocumentCount];

        for (var index = 0; index < _sources.Length; index++)
        {
            _sources[index] = CreateSource(index, Scenario);
        }

        for (var index = 0; index < _sources.Length; index++)
        {
            var legacyKey = LegacyResolveKey(_sources[index]);
            var currentKey = CurrentResolveKey(_sources[index]);
            var legacyDocument = LegacyExtractDocument(_sources[index], TitleFieldName, ContentFieldName);
            var currentDocument = ElasticsearchSourceDocumentMapper.ExtractDocument(
                _sources[index],
                _titleFieldPath,
                _contentFieldPath,
                treatWhitespaceAsEmpty: false);

            EnsureEquivalent(legacyKey, currentKey, legacyDocument, currentDocument, index);
        }
    }

    /// <summary>
    /// Maps the selected source batch with per-document dotted-path splitting.
    /// </summary>
    /// <returns>A checksum over the mapped document keys and fields.</returns>
    [Benchmark(Baseline = true)]
    public int Legacy()
    {
        var checksum = 0;

        foreach (var source in _sources)
        {
            var key = LegacyResolveKey(source);
            var document = LegacyExtractDocument(source, TitleFieldName, ContentFieldName);
            checksum += key.Length + document.Title.Length + document.Content.Length + document.Fields.Count;
        }

        return checksum;
    }

    /// <summary>
    /// Maps the selected source batch with reusable dotted field paths.
    /// </summary>
    /// <returns>A checksum over the mapped document keys and fields.</returns>
    [Benchmark]
    public int Current()
    {
        var checksum = 0;

        foreach (var source in _sources)
        {
            var key = CurrentResolveKey(source);
            var document = ElasticsearchSourceDocumentMapper.ExtractDocument(
                source,
                _titleFieldPath,
                _contentFieldPath,
                treatWhitespaceAsEmpty: false);
            checksum += key.Length + document.Title.Length + document.Content.Length + document.Fields.Count;
        }

        return checksum;
    }

    private static string LegacyResolveKey(JsonObject source)
    {
        return LegacyResolveFieldValue(source, KeyFieldName).GetStringValue();
    }

    private string CurrentResolveKey(JsonObject source)
    {
        return ElasticsearchSourceDocumentMapper.ResolveFieldValue(source, _keyFieldPath).GetStringValue();
    }

    private static SourceDocument LegacyExtractDocument(
        JsonObject source,
        string titleFieldName,
        string contentFieldName)
    {
        string title = null;
        string content = null;

        if (!string.IsNullOrEmpty(titleFieldName))
        {
            var titleNode = LegacyResolveFieldValue(source, titleFieldName);
            title = titleNode.GetStringValue();
        }

        if (!string.IsNullOrEmpty(contentFieldName))
        {
            var contentNode = LegacyResolveFieldValue(source, contentFieldName);
            content = contentNode.GetStringValue();
        }

        if (string.IsNullOrEmpty(content))
        {
            content = source.ToJsonString();
        }

        if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(content))
        {
            title = content.ExtractTitleFromContent();
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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

    private static JsonNode LegacyResolveFieldValue(JsonObject source, string fieldPath)
    {
        if (source == null || string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        if (source.TryGetPropertyValue(fieldPath, out var directNode))
        {
            return directNode;
        }

        if (!fieldPath.Contains('.'))
        {
            return null;
        }

        var segments = fieldPath.Split('.');
        JsonNode current = source;

        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static JsonObject CreateSource(int index, string scenario)
    {
        var source = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["identity"] = new JsonObject
                {
                    ["key"] = $"document-{index}",
                },
            },
            ["content"] = new JsonObject
            {
                ["title"] = $"Mapped title {index}",
                ["body"] = $"Mapped content {index}",
            },
            ["count"] = (long)index,
            ["tags"] = new JsonArray($"tag-{index % 8}", $"group-{index % 4}"),
        };

        if (scenario == "DirectDotted")
        {
            source[KeyFieldName] = $"direct-document-{index}";
            source[TitleFieldName] = $"Direct title {index}";
            source[ContentFieldName] = $"Direct content {index}";
        }

        return source;
    }

    private static void EnsureEquivalent(
        string legacyKey,
        string currentKey,
        SourceDocument legacyDocument,
        SourceDocument currentDocument,
        int index)
    {
        if (!string.Equals(legacyKey, currentKey, StringComparison.Ordinal) ||
            !string.Equals(legacyDocument.Title, currentDocument.Title, StringComparison.Ordinal) ||
            !string.Equals(legacyDocument.Content, currentDocument.Content, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Mapped document scalar output differs at index {index}.");
        }

        if (!legacyDocument.Fields.Comparer.Equals(currentDocument.Fields.Comparer) ||
            !legacyDocument.Fields.Keys.SequenceEqual(currentDocument.Fields.Keys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Mapped document field shape differs at index {index}.");
        }

        foreach (var key in legacyDocument.Fields.Keys)
        {
            EnsureEquivalentValue(legacyDocument.Fields[key], currentDocument.Fields[key], index, key);
        }
    }

    private static void EnsureEquivalentValue(
        object legacy,
        object current,
        int index,
        string key)
    {
        if (legacy == null || current == null)
        {
            if (legacy != current)
            {
                throw new InvalidOperationException($"Mapped document field '{key}' differs at index {index}.");
            }

            return;
        }

        if (legacy.GetType() != current.GetType())
        {
            throw new InvalidOperationException($"Mapped document field '{key}' type differs at index {index}.");
        }

        switch (legacy)
        {
            case Dictionary<string, object> legacyDictionary:
                EnsureEquivalentDictionary(legacyDictionary, (Dictionary<string, object>)current, index, key);
                break;

            case List<object> legacyList:
                EnsureEquivalentList(legacyList, (List<object>)current, index, key);
                break;

            default:
                if (!legacy.Equals(current))
                {
                    throw new InvalidOperationException($"Mapped document field '{key}' value differs at index {index}.");
                }
                break;
        }
    }

    private static void EnsureEquivalentDictionary(
        Dictionary<string, object> legacy,
        Dictionary<string, object> current,
        int index,
        string key)
    {
        if (!legacy.Comparer.Equals(current.Comparer) ||
            !legacy.Keys.SequenceEqual(current.Keys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Mapped document dictionary field '{key}' differs at index {index}.");
        }

        foreach (var childKey in legacy.Keys)
        {
            EnsureEquivalentValue(legacy[childKey], current[childKey], index, $"{key}.{childKey}");
        }
    }

    private static void EnsureEquivalentList(
        List<object> legacy,
        List<object> current,
        int index,
        string key)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException($"Mapped document list field '{key}' length differs at index {index}.");
        }

        for (var childIndex = 0; childIndex < legacy.Count; childIndex++)
        {
            EnsureEquivalentValue(legacy[childIndex], current[childIndex], index, $"{key}[{childIndex}]");
        }
    }
}
