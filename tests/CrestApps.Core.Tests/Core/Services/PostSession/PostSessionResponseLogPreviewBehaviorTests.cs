using System.Reflection;
using CrestApps.Core.AI.Chat.Services;

namespace CrestApps.Core.Tests.Core.Services.PostSession;

/// <summary>
/// Verifies the exact response log preview compatibility contract.
/// </summary>
public sealed class PostSessionResponseLogPreviewBehaviorTests
{
    private const int MaximumPreviewLength = 2_000;

    private static readonly MethodInfo _createResponseLogPreview = typeof(PostSessionProcessingService)
        .GetMethod("CreateResponseLogPreview", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the response log preview helper.");

    /// <summary>
    /// Verifies that null and empty responses use the existing empty marker.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateResponseLogPreview_WhenResponseIsNullOrEmpty_ReturnsEmptyMarker(string responseText)
    {
        Assert.Equal("(empty)", CreateResponseLogPreview(responseText));
    }

    /// <summary>
    /// Verifies exact truncation behavior without carriage returns or line feeds.
    /// </summary>
    /// <param name="responseLength">The response length.</param>
    /// <param name="hasEllipsis">Whether the preview should have an ellipsis.</param>
    [Theory]
    [InlineData(1_999, false)]
    [InlineData(2_000, false)]
    [InlineData(2_001, true)]
    public void CreateResponseLogPreview_WithoutNewlines_UsesExactTwoThousandCharacterBoundary(
        int responseLength,
        bool hasEllipsis)
    {
        var responseText = new string('x', responseLength);
        var expected = new string('x', Math.Min(responseLength, MaximumPreviewLength));

        if (hasEllipsis)
        {
            expected += "...";
        }

        var actual = CreateResponseLogPreview(responseText);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that carriage returns and line feeds are escaped independently and in ordinal order.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <param name="expected">The expected escaped preview.</param>
    [Theory]
    [InlineData("\r", "\\r")]
    [InlineData("\n", "\\n")]
    [InlineData("\r\n", "\\r\\n")]
    [InlineData("\n\r", "\\n\\r")]
    [InlineData("before\rafter", "before\\rafter")]
    [InlineData("before\nafter", "before\\nafter")]
    [InlineData("before\r\nafter", "before\\r\\nafter")]
    public void CreateResponseLogPreview_WithLineEndings_EscapesEachCodeUnit(
        string responseText,
        string expected)
    {
        var actual = CreateResponseLogPreview(responseText);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that escaping occurs before the two-thousand-character truncation decision.
    /// </summary>
    /// <param name="prefixLength">The number of prefix characters.</param>
    /// <param name="suffix">The unescaped suffix.</param>
    /// <param name="escapedSuffix">The escaped suffix.</param>
    /// <param name="normalizedLength">The expected fully normalized length.</param>
    /// <param name="hasEllipsis">Whether the preview should have an ellipsis.</param>
    [Theory]
    [InlineData(1_997, "\n", "\\n", 1_999, false)]
    [InlineData(1_998, "\n", "\\n", 2_000, false)]
    [InlineData(1_999, "\n", "\\n", 2_001, true)]
    [InlineData(1_997, "\r", "\\r", 1_999, false)]
    [InlineData(1_998, "\r", "\\r", 2_000, false)]
    [InlineData(1_999, "\r", "\\r", 2_001, true)]
    [InlineData(1_996, "\r\n", "\\r\\n", 2_000, false)]
    [InlineData(1_997, "\r\n", "\\r\\n", 2_001, true)]
    [InlineData(1_998, "\nx", "\\nx", 2_001, true)]
    public void CreateResponseLogPreview_WhenEscapingChangesLength_TruncatesNormalizedText(
        int prefixLength,
        string suffix,
        string escapedSuffix,
        int normalizedLength,
        bool hasEllipsis)
    {
        var prefix = new string('a', prefixLength);
        var responseText = prefix + suffix;
        var normalized = prefix + escapedSuffix;
        var expected = normalized.Length > MaximumPreviewLength
            ? normalized[..MaximumPreviewLength] + "..."
            : normalized;

        Assert.Equal(normalizedLength, normalized.Length);
        Assert.Equal(hasEllipsis, normalized.Length > MaximumPreviewLength);
        Assert.Equal(expected, CreateResponseLogPreview(responseText));
    }

    /// <summary>
    /// Verifies that Unicode code units, valid surrogate pairs, and lone surrogates remain unchanged.
    /// </summary>
    [Fact]
    public void CreateResponseLogPreview_WithUnicodeAndSurrogates_PreservesUtf16CodeUnits()
    {
        const string responseText = "café—漢字\uD83D\uDE00|\uD800X\uDC00\r\n";
        const string expected = "café—漢字\uD83D\uDE00|\uD800X\uDC00\\r\\n";

        var actual = CreateResponseLogPreview(responseText);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that truncation preserves the exact ordinal UTF-16 prefix, even when it splits a surrogate pair.
    /// </summary>
    [Fact]
    public void CreateResponseLogPreview_WhenBoundarySplitsSurrogatePair_ReturnsExactOrdinalPrefix()
    {
        var responseText = new string('a', MaximumPreviewLength - 1) + "\uD83D\uDE00";
        var expected = responseText[..MaximumPreviewLength] + "...";

        var actual = CreateResponseLogPreview(responseText);

        Assert.Equal(expected, actual);
        Assert.True(actual.AsSpan(0, MaximumPreviewLength).SequenceEqual(responseText.AsSpan(0, MaximumPreviewLength)));
        Assert.True(char.IsHighSurrogate(actual[MaximumPreviewLength - 1]));
        Assert.Equal('.', actual[MaximumPreviewLength]);
    }

    /// <summary>
    /// Verifies exact legacy normalization and truncation for a very large response.
    /// </summary>
    [Fact]
    public void CreateResponseLogPreview_WithVeryLargeResponse_MatchesFullLegacyNormalization()
    {
        var responseText = string.Create(1024 * 1024, 0, static (characters, _) =>
        {
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = (index % 64) switch
                {
                    62 => '\r',
                    63 => '\n',
                    _ => (char)('a' + (index % 26)),
                };
            }
        });
        var normalized = responseText
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        var expected = normalized[..MaximumPreviewLength] + "...";

        var actual = CreateResponseLogPreview(responseText);

        Assert.Equal(MaximumPreviewLength + 3, actual.Length);
        Assert.Equal(expected, actual);
        Assert.True(actual.AsSpan(0, MaximumPreviewLength).SequenceEqual(normalized.AsSpan(0, MaximumPreviewLength)));
    }

    /// <summary>
    /// Invokes the production response log preview helper.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <returns>The response log preview.</returns>
    private static string CreateResponseLogPreview(string responseText)
    {
        return (string)_createResponseLogPreview.Invoke(null, [responseText]);
    }
}
