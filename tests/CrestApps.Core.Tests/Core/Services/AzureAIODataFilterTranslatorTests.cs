using CrestApps.Core.Azure.AISearch.Services;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers how caller filters reach an Azure AI Search index: fields in the filter bag are addressed through
/// it, the typed knowledge columns are addressed directly, and keywords are never mistaken for fields.
/// </summary>
public sealed class AzureAIODataFilterTranslatorTests
{
    private readonly AzureAIODataFilterTranslator _translator = new();

    /// <summary>
    /// Verifies that a typed column is left as a field of its own, while a caller-supplied field goes
    /// through the filter bag.
    /// </summary>
    /// <param name="filter">The filter to translate.</param>
    /// <param name="expected">The expected OData filter.</param>
    [Theory]
    [InlineData("contentType eq 'figure'", "contentType eq 'figure'")]
    [InlineData("contentType eq null", "contentType eq null")]
    [InlineData("(contentType eq 'text' or contentType eq null)", "(contentType eq 'text' or contentType eq null)")]
    [InlineData("page ge 8 and rootId eq 'document:abc'", "page ge 8 and rootId eq 'document:abc'")]
    [InlineData("category eq 'news'", "filters/category eq 'news'")]
    [InlineData("filters/category eq 'news'", "filters/category eq 'news'")]
    public void Translate_TypedColumnsStayColumns_OthersGoToTheFilterBag(string filter, string expected)
    {
        Assert.Equal(expected, _translator.Translate(filter));
    }

    /// <summary>
    /// Verifies that an empty filter translates to nothing.
    /// </summary>
    [Fact]
    public void Translate_Empty_ReturnsNull()
    {
        Assert.Null(_translator.Translate(" "));
    }
}
