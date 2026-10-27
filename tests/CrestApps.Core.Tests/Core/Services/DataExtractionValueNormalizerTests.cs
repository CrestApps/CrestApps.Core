using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DataExtractionValueNormalizerTests
{
    [Fact]
    public void UntypedEntries_KeepTheValueAsExtracted()
    {
        var entry = new DataExtractionEntry { Name = "city" };

        Assert.Equal(DataExtractionFieldType.Text, entry.FieldType);
        Assert.True(DataExtractionValueNormalizer.TryNormalize(entry, "  New York ", out var value));
        Assert.Equal("  New York ", value);
        Assert.False(DataExtractionValueNormalizer.TryNormalize(entry, " ", out _));
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("$1,200.50", "1200.50")]
    [InlineData("15%", "15")]
    [InlineData("-3.5", "-3.5")]
    public void Number_IsNormalizedToInvariantForm(string input, string expected)
    {
        Assert.True(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Number }, input, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("forty")]
    [InlineData("12abc")]
    public void Number_RejectsNonNumbers(string input)
    {
        Assert.False(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Number }, input, out _));
    }

    [Theory]
    [InlineData("Yes", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("no", "false")]
    [InlineData("0", "false")]
    public void Boolean_MapsCommonAnswers(string input, string expected)
    {
        Assert.True(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Boolean }, input, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Boolean_RejectsAnythingElse()
    {
        Assert.False(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Boolean }, "maybe", out _));
    }

    [Theory]
    [InlineData("2026-03-05", "2026-03-05")]
    [InlineData("March 5, 2026", "2026-03-05")]
    public void Date_IsNormalizedToIsoDate(string input, string expected)
    {
        Assert.True(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Date }, input, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Date_RejectsNonDates()
    {
        Assert.False(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Date }, "next-ish", out _));
    }

    [Fact]
    public void Enum_MatchesAllowedValuesCaseInsensitivelyAndKeepsConfiguredSpelling()
    {
        var entry = new DataExtractionEntry { FieldType = DataExtractionFieldType.Enum, AllowedValues = ["Basic", "Premium"] };

        Assert.True(DataExtractionValueNormalizer.TryNormalize(entry, "premium", out var value));
        Assert.Equal("Premium", value);
        Assert.False(DataExtractionValueNormalizer.TryNormalize(entry, "gold", out _));
    }

    [Fact]
    public void Enum_WithoutAllowedValues_KeepsTheValue()
    {
        Assert.True(DataExtractionValueNormalizer.TryNormalize(new DataExtractionEntry { FieldType = DataExtractionFieldType.Enum }, " x ", out var value));
        Assert.Equal("x", value);
    }

    [Fact]
    public void ExistingJson_WithoutFieldType_DeserializesAsText()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<DataExtractionEntry>("""{"Name":"a","Description":"d","AllowMultipleValues":true}""");

        Assert.Equal(DataExtractionFieldType.Text, entry.FieldType);
        Assert.Empty(entry.AllowedValues);
    }
}
