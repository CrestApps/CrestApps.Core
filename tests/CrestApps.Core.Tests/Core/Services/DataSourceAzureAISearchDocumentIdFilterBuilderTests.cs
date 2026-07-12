using CrestApps.Core.Azure.AISearch.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DataSourceAzureAISearchDocumentIdFilterBuilderTests
{
    [Fact]
    public void FilterDocumentIds_EmptyInput_ReturnsEmptyList()
    {
        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.FilterDocumentIds([]);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterDocumentIds_NullAndEmptyIds_AreFilteredWithoutChangingOtherValues()
    {
        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.FilterDocumentIds(
            [
                null,
                string.Empty,
                " ",
                "document",
            ]);

        Assert.Equal([" ", "document"], result);
    }

    [Fact]
    public void FilterDocumentIds_PreservesOrderDuplicatesAndStringIdentity()
    {
        var first = new string(['f', 'i', 'r', 's', 't']);
        var second = new string(['s', 'e', 'c', 'o', 'n', 'd']);

        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.FilterDocumentIds(
            [
                first,
                second,
                first,
            ]);

        Assert.Collection(
            result,
            value => Assert.Same(first, value),
            value => Assert.Same(second, value),
            value => Assert.Same(first, value));
    }

    [Fact]
    public void BuildFilter_ExplicitKeyFieldName_PreservesOrderAndEscapesApostrophes()
    {
        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter(
            ["first", "O'Brien", "last"],
            "documentId");

        Assert.Equal(
            "documentId eq 'first' or documentId eq 'O''Brien' or documentId eq 'last'",
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildFilter_MissingKeyFieldName_ReturnsNull(string keyFieldName)
    {
        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter(["document"], keyFieldName);

        Assert.Null(result);
    }

    [Fact]
    public void BuildFilter_EmptyIds_ReturnsEmptyFilterForExplicitKeyField()
    {
        var result = DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter([], "documentId");

        Assert.Equal(string.Empty, result);
    }
}
