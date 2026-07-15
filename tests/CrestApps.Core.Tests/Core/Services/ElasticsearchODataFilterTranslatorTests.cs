using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Characterizes Elasticsearch OData filter translation behavior.
/// </summary>
public sealed class ElasticsearchODataFilterTranslatorTests
{
    private readonly IODataFilterTranslator _translator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchODataFilterTranslatorTests"/> class.
    /// </summary>
    public ElasticsearchODataFilterTranslatorTests()
    {
        var services = new ServiceCollection();
        services.AddCoreElasticsearchServices();
        var provider = services.BuildServiceProvider();
        _translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(ElasticsearchConstants.ProviderName);
    }

    /// <summary>
    /// Verifies that empty or tokenless filters return <see langword="null"/>.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Translate_EmptyOrTokenlessInput_ReturnsNull(string filter)
    {
        Assert.Null(_translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the exact Elasticsearch query emitted for every supported comparison operator.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData("category eq 'news'", """{"term":{"filters.category":"news"}}""")]
    [InlineData("status ne 'draft'", """{"bool":{"must_not":[{"term":{"filters.status":"draft"}}]}}""")]
    [InlineData("price gt 100", """{"range":{"filters.price":{"gt":"100"}}}""")]
    [InlineData("price ge 100", """{"range":{"filters.price":{"gte":"100"}}}""")]
    [InlineData("price lt 100", """{"range":{"filters.price":{"lt":"100"}}}""")]
    [InlineData("price le 100", """{"range":{"filters.price":{"lte":"100"}}}""")]
    public void Translate_ComparisonOperators_ProduceExactQuery(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the existing left-associative logical parsing and unary-not precedence.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData(
        "a eq '1' and b eq '2'",
        """{"bool":{"must":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}]}}""")]
    [InlineData(
        "a eq '1' or b eq '2'",
        """{"bool":{"should":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}],"minimum_should_match":1}}""")]
    [InlineData(
        "a eq '1' or b eq '2' and c eq '3'",
        """{"bool":{"must":[{"bool":{"should":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}],"minimum_should_match":1}},{"term":{"filters.c":"3"}}]}}""")]
    [InlineData(
        "a eq '1' and b eq '2' or c eq '3'",
        """{"bool":{"should":[{"bool":{"must":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}]}},{"term":{"filters.c":"3"}}],"minimum_should_match":1}}""")]
    [InlineData(
        "not a eq '1' and b eq '2'",
        """{"bool":{"must":[{"bool":{"must_not":[{"term":{"filters.a":"1"}}]}},{"term":{"filters.b":"2"}}]}}""")]
    [InlineData(
        "not (a eq '1' or b eq '2')",
        """{"bool":{"must_not":[{"bool":{"should":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}],"minimum_should_match":1}}]}}""")]
    public void Translate_LogicalOperators_PreserveExistingPrecedence(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies that nested parentheses preserve the existing parser grouping.
    /// </summary>
    [Fact]
    public void Translate_NestedParentheses_PreserveGrouping()
    {
        var result = _translator.Translate("((a eq '1' or b eq '2') and (c gt 3 or d le 4))");

        Assert.Equal(
            """{"bool":{"must":[{"bool":{"should":[{"term":{"filters.a":"1"}},{"term":{"filters.b":"2"}}],"minimum_should_match":1}},{"bool":{"should":[{"range":{"filters.c":{"gt":"3"}}},{"range":{"filters.d":{"lte":"4"}}}],"minimum_should_match":1}}]}}""",
            result);
    }

    /// <summary>
    /// Verifies the exact query emitted for every supported string function.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData(
        "contains(title, 'hello')",
        """{"wildcard":{"filters.title":{"value":"*hello*"}}}""")]
    [InlineData(
        "startswith(title, 'hello')",
        """{"prefix":{"filters.title":{"value":"hello"}}}""")]
    [InlineData(
        "endswith(title, 'world')",
        """{"wildcard":{"filters.title":{"value":"*world"}}}""")]
    public void Translate_StringFunctions_ProduceExactQuery(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies that quoted values containing delimiters and spaces remain single tokens.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData(
        "contains(title, 'hello, (wide) world')",
        """{"wildcard":{"filters.title":{"value":"*hello, (wide) world*"}}}""")]
    [InlineData(
        "title eq 'hello, (wide) world'",
        """{"term":{"filters.title":"hello, (wide) world"}}""")]
    public void Translate_QuotedValuesContainingDelimiters_RemainSingleValues(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the exact field prefixing behavior for simple and nested field names.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData("category eq 'books'", """{"term":{"filters.category":"books"}}""")]
    [InlineData("filters.category eq 'books'", """{"term":{"filters.category":"books"}}""")]
    [InlineData(
        "metadata.category.name eq 'books'",
        """{"term":{"filters.metadata.category.name":"books"}}""")]
    [InlineData(
        "FILTERS.Metadata.Category eq 'books'",
        """{"term":{"FILTERS.Metadata.Category":"books"}}""")]
    public void Translate_FieldNames_PreserveExistingPrefixing(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies that booleans, nulls, and numbers retain the existing string-valued output.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData("enabled eq true", """{"term":{"filters.enabled":"true"}}""")]
    [InlineData("enabled eq false", """{"term":{"filters.enabled":"false"}}""")]
    [InlineData("deletedAt eq null", """{"term":{"filters.deletedAt":"null"}}""")]
    [InlineData("count eq 42", """{"term":{"filters.count":"42"}}""")]
    [InlineData("score ge 4.5", """{"range":{"filters.score":{"gte":"4.5"}}}""")]
    [InlineData("temperature lt -12.5", """{"range":{"filters.temperature":{"lt":"12.5"}}}""")]
    public void Translate_LiteralKinds_PreserveExistingStringOutput(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the existing wildcard escaping and prefix-query behavior.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected Elasticsearch query JSON.</param>
    [Theory]
    [InlineData(
        "contains(pattern, 'a*b?c')",
        """{"wildcard":{"filters.pattern":{"value":"*a\*b\?c*"}}}""")]
    [InlineData(
        "endswith(pattern, 'a*b?c')",
        """{"wildcard":{"filters.pattern":{"value":"*a\*b\?c"}}}""")]
    [InlineData(
        "startswith(pattern, 'a*b?c')",
        """{"prefix":{"filters.pattern":{"value":"a*b?c"}}}""")]
    public void Translate_Wildcards_PreserveExistingEscaping(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the existing JSON escaping for backslashes and double quotes.
    /// </summary>
    [Fact]
    public void Translate_JsonSpecialCharacters_PreserveExistingEscaping()
    {
        var result = _translator.Translate("""path eq 'C:\docs\"quoted"'""");

        Assert.Equal(
            """{"term":{"filters.path":"C:\\docs\\\"quoted\""}}""",
            result);
    }

    /// <summary>
    /// Verifies exact fallback output for malformed or unsupported filters that do not throw.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected fallback or partial query.</param>
    [Theory]
    [InlineData("category", "{}")]
    [InlineData("category eq", "{}")]
    [InlineData("category = 'news'", "{}")]
    [InlineData("category add 'news'", "{}")]
    [InlineData("tolower(category, 'news')", "{}")]
    [InlineData("()", "{}")]
    [InlineData("(category eq 'news'", """{"term":{"filters.category":"news"}}""")]
    [InlineData("category eq 'news')", """{"term":{"filters.category":"news"}}""")]
    [InlineData("category eq 'news", """{"term":{"filters.category":"news"}}""")]
    [InlineData(
        "category eq 'news' and",
        """{"bool":{"must":[{"term":{"filters.category":"news"}},{}]}}""")]
    [InlineData(
        "contains(title)",
        """{"wildcard":{"filters.title":{"value":"*)*"}}}""")]
    public void Translate_MalformedOrUnsupportedInput_PreservesExactFallback(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies the exact exception type and parameter for malformed function calls.
    /// </summary>
    /// <param name="filter">The malformed filter to translate.</param>
    [Theory]
    [InlineData("contains(")]
    [InlineData("contains()")]
    [InlineData("contains(title,")]
    public void Translate_MalformedFunction_ThrowsArgumentOutOfRangeException(string filter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _translator.Translate(filter));

        Assert.Equal("index", exception.ParamName);
    }
}
