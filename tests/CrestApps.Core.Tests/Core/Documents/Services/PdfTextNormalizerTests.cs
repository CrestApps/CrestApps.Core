using CrestApps.Core.AI.Documents.Pdf.Services;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class PdfTextNormalizerTests
{
    /// <summary>
    /// Verifies that ligature glyphs expand to the letters they stand for, that the stray space some
    /// producers emit after a ligature is removed, and that a ligature ending a word keeps its separator.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    /// <param name="expected">The normalized text.</param>
    [Theory]
    [InlineData("proﬁ lokkal", "profilokkal")]
    [InlineData("proﬁlokkal", "profilokkal")]
    [InlineData("coﬀee", "coffee")]
    [InlineData("inﬂation", "inflation")]
    [InlineData("oﬃce", "office")]
    [InlineData("shaﬄe", "shaffle")]
    [InlineData("classiﬁ Cation", "classifi Cation")]
    public void Normalize_Ligatures_AreExpanded(string input, string expected)
    {
        Assert.Equal(expected, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that word separators survive in scripts that have no letter case.
    /// </summary>
    /// <remarks>
    /// Arabic is routinely extracted as presentation forms, so nearly every word both ends in one and is
    /// followed by a caseless letter. Treating a caseless letter as evidence that a word continued removed
    /// the space at every one of those boundaries and ran the whole paragraph into a single token, which no
    /// search could ever match. Only a lower-case letter counts as that evidence.
    /// </remarks>
    /// <param name="input">The extracted text.</param>
    [Theory]
    [InlineData("ﻣلس ﻣلس")]
    [InlineData("ﬁ 中文")]
    [InlineData("ﬂ אב")]
    public void Normalize_CaselessScriptAfterAPresentationForm_KeepsTheSpace(string input)
    {
        Assert.Contains(" ", PdfTextNormalizer.Normalize(input), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a word broken across a line is made whole again. This is the single largest source of
    /// unsearchable words in a typeset document, and it is worse in the languages that build long compounds.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    /// <param name="expected">The normalized text.</param>
    [Theory]
    [InlineData("pro-\nfilokkal", "profilokkal")]
    [InlineData("pro­\nfilokkal", "profilokkal")]
    [InlineData("pro‐\nfilokkal", "profilokkal")]
    [InlineData("Waerme-\n  uebertragung", "Waermeuebertragung")]
    public void Normalize_HyphenatedAcrossALine_IsRejoined(string input, string expected)
    {
        Assert.Equal(expected, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that a hyphen that is not a line break, and a compound that genuinely carries one, both keep
    /// their hyphen.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    /// <param name="expected">The normalized text.</param>
    [Theory]
    [InlineData("state-of-the-art", "state-of-the-art")]
    [InlineData("Nord-\nSued", "Nord-Sued")]
    [InlineData("well- known", "well- known")]
    public void Normalize_HyphenThatIsNotABreak_IsKept(string input, string expected)
    {
        Assert.Equal(expected, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that a word broken across a line is rejoined in a script that has no upper and lower case,
    /// where a rule keyed on capitalisation would never fire.
    /// </summary>
    [Fact]
    public void Normalize_HyphenatedInACaselessScript_IsRejoined()
    {
        // Two Devanagari syllables split by a line-break hyphen.
        Assert.Equal("कल", PdfTextNormalizer.Normalize("क-\nल"));
    }

    /// <summary>
    /// Verifies that accents arriving decomposed are composed, so a word that looks right also compares
    /// right. A search for the composed spelling otherwise misses every occurrence.
    /// </summary>
    [Fact]
    public void Normalize_DecomposedAccents_AreComposed()
    {
        var decomposed = "épületgépészet";

        var normalized = PdfTextNormalizer.Normalize(decomposed);

        Assert.Equal("épületgépészet", normalized);
        Assert.DoesNotContain('́', normalized);
    }

    /// <summary>
    /// Verifies that invisible formatting characters are removed, and that the two that are letters of the
    /// word in Persian and the Indic scripts are kept.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    /// <param name="expected">The normalized text.</param>
    [Theory]
    [InlineData("hid​den", "hidden")]
    [InlineData("﻿mark", "mark")]
    [InlineData("a‫b‬c", "abc")]
    [InlineData("zero‌width", "zero‌width")]
    [InlineData("zero‍width", "zero‍width")]
    public void Normalize_InvisibleCharacters_AreRemovedExceptJoiners(string input, string expected)
    {
        Assert.Equal(expected, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that an Arabic presentation form is expanded back to the letter it stands for, so the word
    /// is searchable by its ordinary spelling.
    /// </summary>
    [Fact]
    public void Normalize_ArabicPresentationForm_IsExpanded()
    {
        // ARABIC LETTER BEH ISOLATED FORM expands to ARABIC LETTER BEH.
        Assert.Equal("ب", PdfTextNormalizer.Normalize("ﺏ"));
    }

    /// <summary>
    /// Verifies that runs of whitespace collapse to one space, that a run holding a line break collapses to
    /// one line break, and that the result is trimmed. The line breaks matter: a table of contents is one
    /// block whose every line is a title and a page number, and it can only be read line by line.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    /// <param name="expected">The normalized text.</param>
    [Theory]
    [InlineData("  alpha   bravo  ", "alpha bravo")]
    [InlineData("alpha\n\nbravo", "alpha\nbravo")]
    [InlineData("alpha \r\n  bravo", "alpha\nbravo")]
    [InlineData("alpha\t \tbravo", "alpha bravo")]
    [InlineData("\r\nalpha\r\n", "alpha")]
    [InlineData("First title  14\nSecond title  18", "First title 14\nSecond title 18")]
    public void Normalize_Whitespace_IsCollapsedAndTrimmed(string input, string expected)
    {
        Assert.Equal(expected, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that numbers survive untouched. Compatibility normalization over a whole string would
    /// rewrite a superscript, turning a coefficient into a different number, and a decimal comma is a decimal
    /// separator in much of the world.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    [Theory]
    [InlineData("R² = 0,9412")]
    [InlineData("y = 1,0451x")]
    [InlineData("1.234,56")]
    [InlineData("2,5 m³/h")]
    public void Normalize_Numbers_ArePreserved(string input)
    {
        Assert.Equal(input, PdfTextNormalizer.Normalize(input));
    }

    /// <summary>
    /// Verifies that a null or empty input is returned unchanged rather than throwing.
    /// </summary>
    /// <param name="input">The extracted text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_NullOrEmpty_IsReturnedUnchanged(string input)
    {
        Assert.Equal(input, PdfTextNormalizer.Normalize(input));
    }
}
