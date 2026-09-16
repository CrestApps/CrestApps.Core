using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.PostgreSQL;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class PostgreSQLODataFilterTranslatorTests
{
    private readonly IODataFilterTranslator _translator;

    public PostgreSQLODataFilterTranslatorTests()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddCorePostgreSQLServices();
        var provider = services.BuildServiceProvider();
        _translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(PostgreSQLConstants.ProviderName);
    }

    [Fact]
    public void Translate_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(_translator.Translate(null));
        Assert.Null(_translator.Translate(""));
        Assert.Null(_translator.Translate("   "));
    }

    [Fact]
    public void Translate_EqOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("category eq 'news'");

        Assert.Contains("=", result);
        Assert.Contains("news", result);
    }

    [Fact]
    public void Translate_NeOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("status ne 'draft'");

        Assert.Contains("<>", result);
        Assert.Contains("draft", result);
    }

    [Fact]
    public void Translate_GtOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("price gt '100'");

        Assert.Contains(">", result);
        Assert.Contains("100", result);
    }

    [Fact]
    public void Translate_AndOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("category eq 'news' and status eq 'published'");

        Assert.Contains("AND", result);
        Assert.Contains("news", result);
        Assert.Contains("published", result);
    }

    [Fact]
    public void Translate_OrOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("category eq 'news' or category eq 'blog'");

        Assert.Contains("OR", result);
        Assert.Contains("news", result);
        Assert.Contains("blog", result);
    }

    [Fact]
    public void Translate_NotOperator_ProducesCorrectSql()
    {
        var result = _translator.Translate("not status eq 'draft'");

        Assert.Contains("NOT", result);
        Assert.Contains("draft", result);
    }

    [Fact]
    public void Translate_ContainsFunction_ProducesILIKE()
    {
        var result = _translator.Translate("contains(title, 'hello')");

        Assert.Contains("ILIKE", result);
        Assert.Contains("hello", result);
        Assert.Contains("%", result);
    }

    [Fact]
    public void Translate_StartsWithFunction_ProducesILIKE()
    {
        var result = _translator.Translate("startswith(title, 'hello')");

        Assert.Contains("ILIKE", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Translate_EndsWithFunction_ProducesILIKE()
    {
        var result = _translator.Translate("endswith(title, 'world')");

        Assert.Contains("ILIKE", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void Translate_FilterField_UsesJsonPathOnFiltersColumn()
    {
        var result = _translator.Translate("filters.category eq 'books'");

        Assert.Contains("\"filters\"#>>'{category}'", result);
        Assert.Contains("books", result);
    }

    [Fact]
    public void Translate_NestedFilterField_UsesNestedJsonPathOnFiltersColumn()
    {
        var result = _translator.Translate("filters.category.subcategory eq 'books'");

        Assert.Contains("\"filters\"#>>'{category,subcategory}'", result);
        Assert.Contains("books", result);
    }

    [Fact]
    public void Translate_NestedParentheses_PreservesGrouping()
    {
        var result = _translator.Translate("(category eq 'news' or category eq 'blog') and status ne 'draft'");

        Assert.Equal(
            "(((\"filters\"#>>'{category}' = 'news' OR \"filters\"#>>'{category}' = 'blog')) AND \"filters\"#>>'{status}' <> 'draft')",
            result);
    }

    [Fact]
    public void Translate_UppercaseOperators_AreHandledCaseInsensitively()
    {
        var result = _translator.Translate("CATEGORY EQ 'news' AND STATUS NE 'draft'");

        Assert.Equal(
            "(\"filters\"#>>'{CATEGORY}' = 'news' AND \"filters\"#>>'{STATUS}' <> 'draft')",
            result);
    }

    [Fact]
    public void Translate_FunctionValueContainingComma_RemainsSingleValue()
    {
        var result = _translator.Translate("contains(title, 'hello, world')");

        Assert.Equal("\"filters\"#>>'{title}' ILIKE '%hello, world%'", result);
    }

    [Fact]
    public void Translate_IncompleteExpression_ReturnsTrueFragment()
    {
        var result = _translator.Translate("category");

        Assert.Equal("TRUE", result);
    }

    [Fact]
    public void SanitizeTableName_InvalidCharacters_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => PostgreSQLHelpers.SanitizeTableName("my\"table';DROP--"));
    }

    [Fact]
    public void SanitizeTableName_LowercasesName()
    {
        var result = PostgreSQLHelpers.SanitizeTableName("MyTableName");

        Assert.Equal("mytablename", result);
    }

    [Fact]
    public void SanitizeColumnName_InvalidCharacters_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => PostgreSQLHelpers.SanitizeColumnName("my\"column"));
    }
}

/// <summary>
/// Covers the typed knowledge columns, which are real columns rather than entries in the filter bag, and
/// the null comparison a caller uses to reach rows written before those columns existed.
/// </summary>
public sealed class PostgreSQLODataFilterTranslatorTypedColumnTests
{
    private readonly IODataFilterTranslator _translator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLODataFilterTranslatorTypedColumnTests"/> class.
    /// </summary>
    public PostgreSQLODataFilterTranslatorTypedColumnTests()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddCorePostgreSQLServices();
        var provider = services.BuildServiceProvider();
        _translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(PostgreSQLConstants.ProviderName);
    }

    /// <summary>
    /// Verifies that a filter on a typed column reaches the column itself. Looking it up in the filter bag
    /// would match nothing, because the value was promoted out of the bag when it became a column.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected SQL fragment.</param>
    [Theory]
    [InlineData("contentType eq 'figure'", "\"contentType\" = 'figure'")]
    [InlineData("rootId eq 'document:abc'", "\"rootId\" = 'document:abc'")]
    [InlineData("parentId ne 'article:abc:1'", "\"parentId\" <> 'article:abc:1'")]
    [InlineData("page ge 8", "\"page\" >= '8'")]
    public void Translate_TypedColumn_TargetsTheColumnNotTheFilterBag(string filter, string expected)
    {
        var result = _translator.Translate(filter);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("filters", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that comparing to null becomes a null test. A row indexed before the column existed has no
    /// value, and <c>= 'null'</c> would never find it.
    /// </summary>
    [Fact]
    public void Translate_EqNull_BecomesIsNull()
    {
        Assert.Equal("\"contentType\" IS NULL", _translator.Translate("contentType eq null"));
        Assert.Equal("\"contentType\" IS NOT NULL", _translator.Translate("contentType ne null"));
    }

    /// <summary>
    /// Verifies the filter retrieval composes for "text only", which has to admit legacy rows as well.
    /// </summary>
    [Fact]
    public void Translate_TextOrNull_ProducesBothClauses()
    {
        var result = _translator.Translate("(contentType eq 'text' or contentType eq null)");

        Assert.Equal("((\"contentType\" = 'text' OR \"contentType\" IS NULL))", result);
    }

    /// <summary>
    /// Verifies that a field that is not a column still goes to the filter bag, so nothing about the
    /// existing behaviour for caller-supplied fields changed.
    /// </summary>
    [Fact]
    public void Translate_UnknownField_StillTargetsTheFilterBag()
    {
        var result = _translator.Translate("category eq 'news'");

        Assert.Contains("\"filters\"", result, StringComparison.Ordinal);
    }
}
