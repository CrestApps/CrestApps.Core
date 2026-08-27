using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Pins how the importer decides a value is numeric and how it normalizes it for storage, so numbers
/// stored as text in a spreadsheet (currency, thousands-separated, accounting negatives) still compute
/// correctly, while genuinely non-numeric or identifier-like values keep their original text.
/// </summary>
public sealed class TabularNumericNormalizationTests
{
    [Theory]
    [InlineData("100", "100", true)]
    [InlineData("-50", "-50", true)]
    [InlineData("+50", "+50", true)]
    [InlineData("1.5", "1.5", false)]
    [InlineData("1.2E3", "1.2E3", false)]
    [InlineData("$1,234.50", "1234.50", false)]
    [InlineData("$1000", "1000", true)]
    [InlineData("1,000", "1000", true)]
    [InlineData("1,234,567", "1234567", true)]
    [InlineData("(100)", "-100", true)]
    [InlineData("(1,234.00)", "-1234.00", false)]
    [InlineData("  42  ", "42", true)]
    public void TryNormalizeNumeric_NumericLikeText_NormalizesToPlainNumber(string input, string expected, bool expectedInteger)
    {
        var parsed = TabularWorkspaceSqliteHelpers.TryNormalizeNumeric(input, out var normalized, out var isInteger);

        Assert.True(parsed);
        Assert.Equal(expected, normalized);
        Assert.Equal(expectedInteger, isInteger);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("abc")]
    [InlineData("12%")]        // percentages carry an ambiguous scale, kept as text
    [InlineData("007")]        // leading-zero identifier (zip/account), kept as text
    [InlineData("00123")]
    [InlineData("1.2.3")]
    [InlineData("(5")]         // unbalanced parenthesis
    [InlineData("-(5)")]       // contradictory sign and parentheses
    public void TryNormalizeNumeric_NonNumericOrAmbiguous_ReturnsFalse(string input)
    {
        Assert.False(TabularWorkspaceSqliteHelpers.TryNormalizeNumeric(input, out _, out _));
    }

    [Fact]
    public void NormalizeCellValue_NumericColumn_NormalizesValue()
    {
        Assert.Equal("1234.50", TabularWorkspaceSqliteHelpers.NormalizeCellValue("REAL", "$1,234.50"));
        Assert.Equal("-100", TabularWorkspaceSqliteHelpers.NormalizeCellValue("INTEGER", "(100)"));
    }

    [Fact]
    public void NormalizeCellValue_TextColumn_LeavesValueUnchanged()
    {
        // A TEXT column preserves the original text verbatim, even when it looks numeric.
        Assert.Equal("$1,234.50", TabularWorkspaceSqliteHelpers.NormalizeCellValue("TEXT", "$1,234.50"));
    }

    [Fact]
    public void NormalizeCellValue_NumericColumnWithUnparseableValue_LeavesValueUnchanged()
    {
        // A stray non-numeric value in a numeric column is preserved rather than dropped; SQLite stores
        // it as text in that cell and the import does not fail.
        Assert.Equal("N/A", TabularWorkspaceSqliteHelpers.NormalizeCellValue("INTEGER", "N/A"));
    }
}
