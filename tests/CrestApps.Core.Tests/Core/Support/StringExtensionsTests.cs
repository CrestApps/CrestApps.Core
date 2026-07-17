using CrestApps.Core.Support;

namespace CrestApps.Core.Tests.Core.Support;

/// <summary>
/// Tests the content-title extraction compatibility contract.
/// </summary>
public sealed class StringExtensionsTests
{
    /// <summary>
    /// Verifies that null content produces an empty title.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_NullContent_ReturnsEmptyString()
    {
        string content = null;

        var title = content.ExtractTitleFromContent();

        Assert.Same(string.Empty, title);
    }

    /// <summary>
    /// Verifies that empty content produces an empty title.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_EmptyContent_ReturnsEmptyString()
    {
        var title = string.Empty.ExtractTitleFromContent();

        Assert.Same(string.Empty, title);
    }

    /// <summary>
    /// Verifies that whitespace-only content produces an empty title.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_WhitespaceContent_ReturnsEmptyString()
    {
        var title = " \t\u00A0\u2003\r\n".ExtractTitleFromContent();

        Assert.Same(string.Empty, title);
    }

    /// <summary>
    /// Verifies that carriage return, line feed, and carriage-return/line-feed sequences terminate
    /// the title independently of the platform line-ending convention.
    /// </summary>
    /// <param name="lineEnding">The line ending to test.</param>
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ExtractTitleFromContent_PositiveLineEndingIndex_ReturnsTrimmedFirstLine(string lineEnding)
    {
        var content = $" \tTitle\u2003{lineEnding}Body{Environment.NewLine}More";

        var title = content.ExtractTitleFromContent();

        Assert.Equal("Title", title);
    }

    /// <summary>
    /// Verifies that a line ending at index zero retains the remaining content before trimming.
    /// </summary>
    /// <param name="lineEnding">The leading line ending to test.</param>
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ExtractTitleFromContent_LeadingLineEnding_PreservesCurrentGreaterThanZeroBehavior(string lineEnding)
    {
        var content = $"{lineEnding}Title{lineEnding}Body";

        var title = content.ExtractTitleFromContent();

        Assert.Equal($"Title{lineEnding}Body", title);
    }

    /// <summary>
    /// Verifies that exactly 200 UTF-16 code units are retained without truncation.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_ExactlyMaximumLength_ReturnsAllCodeUnits()
    {
        var content = new string('a', 200);

        var title = content.ExtractTitleFromContent();

        Assert.Equal(content, title);
        Assert.NotSame(content, title);
    }

    /// <summary>
    /// Verifies that content longer than 200 UTF-16 code units is truncated to the first 200.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_OverMaximumLength_ReturnsFirstTwoHundredCodeUnits()
    {
        var content = new string('a', 200) + "b";

        var title = content.ExtractTitleFromContent();

        Assert.Equal(new string('a', 200), title);
    }

    /// <summary>
    /// Verifies that leading whitespace counts toward the 200-code-unit cap before trimming.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_LeadingWhitespace_IsCappedBeforeTrim()
    {
        var content = "  " + new string('a', 199);

        var title = content.ExtractTitleFromContent();

        Assert.Equal(new string('a', 198), title);
    }

    /// <summary>
    /// Verifies that trailing whitespace at the cap is trimmed after later content is discarded.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_TrailingWhitespaceAtCap_IsTrimmedAfterCap()
    {
        var content = new string('a', 199) + " \tignored";

        var title = content.ExtractTitleFromContent();

        Assert.Equal(new string('a', 199), title);
    }

    /// <summary>
    /// Verifies that tabs and Unicode whitespace are trimmed from both title edges.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_TabsAndUnicodeWhitespace_TrimsBothEdges()
    {
        var title = "\t\u00A0\u2003Title\u2028\u2029\t".ExtractTitleFromContent();

        Assert.Equal("Title", title);
    }

    /// <summary>
    /// Verifies that the UTF-16 cap can retain only the high surrogate of a surrogate pair.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_SurrogatePairCrossesCap_PreservesSplitSurrogate()
    {
        var content = new string('a', 199) + "\U0001F600";

        var title = content.ExtractTitleFromContent();

        Assert.Equal(200, title.Length);
        Assert.Equal('\uD83D', title[^1]);
    }

    /// <summary>
    /// Verifies that large single-line documents return only the first 200 code units.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_LargeDocumentWithoutNewline_ReturnsBoundedPrefix()
    {
        var content = new string('x', 1_048_576);

        var title = content.ExtractTitleFromContent();

        Assert.Equal(new string('x', 200), title);
    }

    /// <summary>
    /// Verifies that a clean title is returned as an equal but independent string.
    /// </summary>
    [Fact]
    public void ExtractTitleFromContent_CleanTitle_ReturnsIndependentEquivalentString()
    {
        var content = string.Concat("Clean", " title");

        var title = content.ExtractTitleFromContent();

        Assert.Equal(content, title);
        Assert.NotSame(content, title);
    }

    /// <summary>
    /// Verifies that null log content sanitizes to an empty string.
    /// </summary>
    [Fact]
    public void SanitizeForLog_NullValue_ReturnsEmptyString()
    {
        string value = null;

        var sanitized = value.SanitizeForLog();

        Assert.Same(string.Empty, sanitized);
    }

    /// <summary>
    /// Verifies that content without line breaks is preserved exactly.
    /// </summary>
    [Fact]
    public void SanitizeForLog_ValueWithoutLineBreaks_ReturnsOriginalValue()
    {
        var value = "Provider-01: ready\t✓";

        var sanitized = value.SanitizeForLog();

        Assert.Equal(value, sanitized);
        Assert.Same(value, sanitized);
    }

    /// <summary>
    /// Verifies that carriage returns and line feeds are removed without changing other characters.
    /// </summary>
    [Theory]
    [InlineData("alpha\rbeta", "alphabeta")]
    [InlineData("alpha\nbeta", "alphabeta")]
    [InlineData("alpha\r\nbeta", "alphabeta")]
    [InlineData("\r\nalpha\n\rbeta\r\n", "alphabeta")]
    [InlineData("alpha\u2028beta\u2029\ngamma\rdelta", "alpha\u2028beta\u2029gammadelta")]
    public void SanitizeForLog_LineBreaks_RemovesOnlyCarriageReturnsAndLineFeeds(string value, string expected)
    {
        var sanitized = value.SanitizeForLog();

        Assert.Equal(expected, sanitized);
    }

    /// <summary>
    /// Verifies that empty content remains empty.
    /// </summary>
    [Fact]
    public void SanitizeForLog_EmptyValue_ReturnsEmptyString()
    {
        var sanitized = string.Empty.SanitizeForLog();

        Assert.Same(string.Empty, sanitized);
    }

}
