using CrestApps.Core.AI.Documents.Ingestion;

namespace CrestApps.Core.Tests.Core.Documents.Ingestion;

public sealed class TextSegmentationTests
{
    /// <summary>
    /// Verifies that a sentence boundary is recognized in scripts that do not end sentences with a full stop.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("First sentence. Second sentence.")]
    [InlineData("First sentence! Second sentence.")]
    [InlineData("一。二。")]
    [InlineData("कल। कल।")]
    [InlineData("كلمة؟ كلمة")]
    [InlineData("การ။ การ")]
    public void ContainsSentenceBoundary_MultipleSentences_IsDetected(string text)
    {
        Assert.True(TextSegmentation.ContainsSentenceBoundary(text));
    }

    /// <summary>
    /// Verifies that a single phrase is not mistaken for two sentences. A running head must stay eligible to
    /// be recognized as decoration.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("QUARTERLY REVIEW")]
    [InlineData("1. ábra a mért értékek")]
    [InlineData("1.234,56")]
    [InlineData("कल कल")]
    public void ContainsSentenceBoundary_SinglePhrase_IsNotDetected(string text)
    {
        Assert.False(TextSegmentation.ContainsSentenceBoundary(text));
    }

    /// <summary>
    /// Verifies that a terminator that needs no space still ends a sentence, which is how the scripts that
    /// write without spaces work.
    /// </summary>
    [Fact]
    public void SplitSentences_IdeographicFullStop_SplitsWithoutWhitespace()
    {
        var sentences = TextSegmentation.SplitSentences("一。二。").ToList();

        Assert.Equal(2, sentences.Count);
        Assert.Equal("一。", sentences[0]);
    }

    /// <summary>
    /// Verifies that a full stop inside a number does not split the text.
    /// </summary>
    [Fact]
    public void SplitSentences_DecimalPoint_DoesNotSplit()
    {
        var sentences = TextSegmentation.SplitSentences("The value is 3.14 exactly.").ToList();

        Assert.Single(sentences);
    }

    /// <summary>
    /// Verifies that letters in a script with no case are reported as caseless, which is what lets the
    /// sentence and hyphenation rules work outside the Latin alphabet.
    /// </summary>
    /// <param name="value">The letter.</param>
    /// <param name="expected">Whether the letter has no case.</param>
    [Theory]
    [InlineData('a', false)]
    [InlineData('A', false)]
    [InlineData('क', true)]
    [InlineData('一', true)]
    [InlineData('ب', true)]
    public void IsCaseless_MatchesTheScript(char value, bool expected)
    {
        Assert.Equal(expected, TextSegmentation.IsCaseless(value));
    }
}
