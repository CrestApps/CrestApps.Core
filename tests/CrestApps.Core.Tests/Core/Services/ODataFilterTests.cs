using CrestApps.Core.Azure.AISearch;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers the public OData filter helper that every Azure AI Search filter is built through.
/// </summary>
public sealed class ODataFilterTests
{
    [Fact]
    public void Quote_ValueWithSingleQuote_DoublesTheQuote()
    {
        Assert.Equal("'O''Brien'", ODataFilter.Quote("O'Brien"));
    }

    [Fact]
    public void Quote_Null_ReturnsTheNullLiteral()
    {
        Assert.Equal("null", ODataFilter.Quote(null));
    }

    [Fact]
    public void Escape_ValueWithSingleQuotes_DoublesEveryQuote()
    {
        Assert.Equal("a''b''''c", ODataFilter.Escape("a'b''c"));
    }

    /// <summary>
    /// Verifies that a value written to break out of its literal stays one literal, so it cannot widen the
    /// filter it is placed in.
    /// </summary>
    [Fact]
    public void Equal_ValueThatTriesToCloseTheLiteral_StaysInsideTheLiteral()
    {
        var filter = ODataFilter.Equal("userId", "x' or userId ne 'x");

        Assert.Equal("userId eq 'x'' or userId ne ''x'", filter);
    }

    [Fact]
    public void Equal_PlainValue_BuildsTheComparison()
    {
        Assert.Equal("dataSourceId eq 'ds1'", ODataFilter.Equal("dataSourceId", "ds1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Equal_MissingFieldName_Throws(string fieldName)
    {
        Assert.ThrowsAny<ArgumentException>(() => ODataFilter.Equal(fieldName, "value"));
    }

    [Fact]
    public void AnyEqual_MatchesChainedEqualComparisons()
    {
        string[] values = ["a", "O'Brien", "", "it''s", "a"];

        var filter = ODataFilter.AnyEqual("referenceId", values);

        Assert.Equal(string.Join(" or ", values.Select(value => ODataFilter.Equal("referenceId", value))), filter);
    }

    [Fact]
    public void AnyEqual_NoValues_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, ODataFilter.AnyEqual("referenceId", []));
    }

    [Fact]
    public void AnyEqual_NullValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ODataFilter.AnyEqual("referenceId", ["a", null]));
    }
}
