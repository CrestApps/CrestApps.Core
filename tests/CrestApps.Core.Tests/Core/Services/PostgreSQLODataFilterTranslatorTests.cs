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
    public void SanitizeTableName_RemovesDangerousCharacters()
    {
        var result = PostgreSQLHelpers.SanitizeTableName("my\"table';DROP--");

        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("'", result);
        Assert.DoesNotContain(";", result);
        Assert.Equal("mytabledrop--", result);
    }

    [Fact]
    public void SanitizeTableName_LowercasesName()
    {
        var result = PostgreSQLHelpers.SanitizeTableName("MyTableName");

        Assert.Equal("mytablename", result);
    }
}
