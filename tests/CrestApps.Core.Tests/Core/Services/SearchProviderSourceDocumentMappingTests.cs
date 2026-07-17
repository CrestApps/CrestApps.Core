using System.Reflection;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.PostgreSQL;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class SearchProviderSourceDocumentMappingTests
{
    private static readonly Type PostgreSQLReaderType = GetRequiredType(
        typeof(PostgreSQLHelpers).Assembly,
        "CrestApps.Core.PostgreSQL.Services.DataSourcePostgreSQLDocumentReader");

    private static readonly Type PostgreSQLHandlerType = GetRequiredType(
        typeof(AI.PostgreSQL.ServiceCollectionExtensions).Assembly,
        "CrestApps.Core.AI.PostgreSQL.Services.PostgreSQLAIDataSourceSourceHandler");

    private static readonly Type ElasticsearchReaderType = GetRequiredType(
        typeof(Elasticsearch.ElasticsearchConstants).Assembly,
        "CrestApps.Core.Elasticsearch.Services.DataSourceElasticsearchDocumentReader");

    private static readonly Type ElasticsearchHandlerType = GetRequiredType(
        typeof(AI.Elasticsearch.ServiceCollectionExtensions).Assembly,
        "CrestApps.Core.AI.Elasticsearch.Services.ElasticsearchAIDataSourceSourceHandler");

    /// <summary>
    /// Verifies PostgreSQL source mapping preserves selected fields, fallback keys, and all row values.
    /// </summary>
    [Theory]
    [MemberData(nameof(PostgreSQLMapperTypes))]
    public void PostgreSQLExtractDocument_ShouldPreserveMappedValues(Type mapperType)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "document-1",
            ["Title"] = "Mapped title",
            ["Body"] = "Mapped content",
            ["Count"] = 42,
            ["Optional"] = null,
        };

        var document = Invoke<SourceDocument>(
            mapperType,
            "ExtractDocument",
            row,
            "title",
            "body");
        var key = Invoke<string>(
            mapperType,
            "ResolveKey",
            row,
            "missing");

        Assert.Equal("document-1", key);
        Assert.Equal("Mapped title", document.Title);
        Assert.Equal("Mapped content", document.Content);
        Assert.Equal(row.Count, document.Fields.Count);
        Assert.Equal(42, document.Fields["COUNT"]);
        Assert.Null(document.Fields["optional"]);
        Assert.Equal(StringComparer.OrdinalIgnoreCase, document.Fields.Comparer);
    }

    /// <summary>
    /// Verifies the two PostgreSQL reader families preserve their existing whitespace fallback rules.
    /// </summary>
    [Fact]
    public void PostgreSQLExtractDocument_ShouldPreserveWhitespaceSemantics()
    {
        var coreRow = CreateWhitespaceRow();
        var aiRow = CreateWhitespaceRow();

        var coreDocument = Invoke<SourceDocument>(
            PostgreSQLReaderType,
            "ExtractDocument",
            coreRow,
            "title",
            "body");
        var aiDocument = Invoke<SourceDocument>(
            PostgreSQLHandlerType,
            "ExtractDocument",
            aiRow,
            "title",
            "body");

        Assert.Equal("   ", coreDocument.Content);
        Assert.NotEqual("   ", aiDocument.Content);
        Assert.Contains("\"body\":\"   \"", aiDocument.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Elasticsearch dotted paths preserve direct-property precedence over nested traversal.
    /// </summary>
    [Theory]
    [MemberData(nameof(ElasticsearchMapperTypes))]
    public void ElasticsearchResolveFieldValue_ShouldPreferDirectDottedProperty(Type mapperType)
    {
        var source = new JsonObject
        {
            ["content.value"] = "direct",
            ["content"] = new JsonObject
            {
                ["value"] = "nested",
            },
        };

        var value = Invoke<JsonNode>(
            mapperType,
            "ResolveFieldValue",
            source,
            "content.value");

        Assert.Equal("direct", value.GetValue<string>());
    }

    /// <summary>
    /// Verifies Elasticsearch source mapping preserves nested values and detached raw fields.
    /// </summary>
    [Theory]
    [MemberData(nameof(ElasticsearchMapperTypes))]
    public void ElasticsearchExtractDocument_ShouldPreserveMappedValues(Type mapperType)
    {
        var source = new JsonObject
        {
            ["id"] = "document-1",
            ["content"] = new JsonObject
            {
                ["title"] = "Mapped title",
                ["body"] = "Mapped content",
            },
            ["count"] = 42L,
            ["tags"] = new JsonArray("one", "two"),
        };

        var document = Invoke<SourceDocument>(
            mapperType,
            "ExtractDocument",
            source,
            "content.title",
            "content.body");

        Assert.Equal("Mapped title", document.Title);
        Assert.Equal("Mapped content", document.Content);
        Assert.Equal(42L, document.Fields["COUNT"]);
        Assert.Equal(["one", "two"], Assert.IsType<List<object>>(document.Fields["tags"]));
        Assert.Equal(StringComparer.OrdinalIgnoreCase, document.Fields.Comparer);
    }

    /// <summary>
    /// Verifies the two Elasticsearch reader families preserve their existing whitespace fallback rules.
    /// </summary>
    [Fact]
    public void ElasticsearchExtractDocument_ShouldPreserveWhitespaceSemantics()
    {
        var coreSource = CreateWhitespaceSource();
        var aiSource = CreateWhitespaceSource();

        var coreDocument = Invoke<SourceDocument>(
            ElasticsearchReaderType,
            "ExtractDocument",
            coreSource,
            "title",
            "body");
        var aiDocument = Invoke<SourceDocument>(
            ElasticsearchHandlerType,
            "ExtractDocument",
            aiSource,
            "title",
            "body");

        Assert.Equal("   ", coreDocument.Content);
        Assert.NotEqual("   ", aiDocument.Content);
        Assert.Contains("\"body\":\"   \"", aiDocument.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the PostgreSQL mapper types covered by the shared behavior tests.
    /// </summary>
    public static TheoryData<Type> PostgreSQLMapperTypes =>
        new()
        {
            PostgreSQLReaderType,
            PostgreSQLHandlerType,
        };

    /// <summary>
    /// Gets the Elasticsearch mapper types covered by the shared behavior tests.
    /// </summary>
    public static TheoryData<Type> ElasticsearchMapperTypes =>
        new()
        {
            ElasticsearchReaderType,
            ElasticsearchHandlerType,
        };

    /// <summary>
    /// Creates a PostgreSQL row with whitespace-only selected values.
    /// </summary>
    /// <returns>The source row.</returns>
    private static Dictionary<string, object> CreateWhitespaceRow()
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "document-1",
            ["title"] = string.Empty,
            ["body"] = "   ",
        };
    }

    /// <summary>
    /// Creates an Elasticsearch source with whitespace-only selected values.
    /// </summary>
    /// <returns>The source document.</returns>
    private static JsonObject CreateWhitespaceSource()
    {
        return new JsonObject
        {
            ["id"] = "document-1",
            ["title"] = string.Empty,
            ["body"] = "   ",
        };
    }

    /// <summary>
    /// Gets a required provider implementation type.
    /// </summary>
    /// <param name="assembly">The provider assembly.</param>
    /// <param name="typeName">The fully qualified type name.</param>
    /// <returns>The resolved type.</returns>
    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true);
    }

    /// <summary>
    /// Invokes a required private static provider mapping method.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="type">The provider implementation type.</param>
    /// <param name="methodName">The method name.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>The method result.</returns>
    private static T Invoke<T>(
        Type type,
        string methodName,
        params object[] arguments)
    {
        var method = type.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return (T)method.Invoke(null, arguments);
    }
}
