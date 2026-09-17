using CrestApps.Core.AI.Documents.Generation.RichText;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class RichTextParserTests
{
    /// <summary>
    /// Verifies that hard-wrapped lines join into one paragraph. Treating every line as its own
    /// paragraph is what made generated documents read as a stack of stranded fragments.
    /// </summary>
    [Fact]
    public void Parse_WrappedLines_BecomeOneParagraph()
    {
        var blocks = RichTextParser.Parse("The first line\ncontinues here.\n\nA second paragraph.");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("The first line continues here.", Flatten(blocks[0]));
        Assert.Equal("A second paragraph.", Flatten(blocks[1]));
    }

    /// <summary>
    /// Verifies that headings are recognized at every depth.
    /// </summary>
    [Theory]
    [InlineData("# Title", 1, "Title")]
    [InlineData("### Section", 3, "Section")]
    [InlineData("###### Deep", 6, "Deep")]
    public void Parse_Heading_IsRecognized(string markdown, int level, string text)
    {
        var block = Assert.Single(RichTextParser.Parse(markdown));

        Assert.Equal(RichTextBlockKind.Heading, block.Kind);
        Assert.Equal(level, block.Level);
        Assert.Equal(text, Flatten(block));
    }

    /// <summary>
    /// Verifies that a hash without a following space stays ordinary text, since that is how a model
    /// writes a reference such as a ticket number.
    /// </summary>
    [Fact]
    public void Parse_HashWithoutSpace_IsNotAHeading()
    {
        var block = Assert.Single(RichTextParser.Parse("#1234 was closed"));

        Assert.Equal(RichTextBlockKind.Paragraph, block.Kind);
    }

    /// <summary>
    /// Verifies that bulleted and numbered lists are recognized and that numbering is sequential.
    /// </summary>
    [Fact]
    public void Parse_Lists_AreRecognized()
    {
        var blocks = RichTextParser.Parse("- first\n- second\n\n1. one\n2. two\n3. three");

        Assert.Equal(RichTextBlockKind.BulletItem, blocks[0].Kind);
        Assert.Equal("first", Flatten(blocks[0]));
        Assert.Equal(RichTextBlockKind.BulletItem, blocks[1].Kind);

        Assert.Equal(RichTextBlockKind.NumberedItem, blocks[2].Kind);
        Assert.Equal([1, 2, 3], blocks.Skip(2).Select(block => block.Number));
    }

    /// <summary>
    /// Verifies that inline emphasis becomes formatting rather than staying as visible asterisks.
    /// </summary>
    [Fact]
    public void Parse_InlineEmphasis_BecomesFormatting()
    {
        var spans = RichTextParser.ParseInline("Plain **bold** and *italic* and `code`.");

        Assert.DoesNotContain(spans, span => span.Text.Contains('*', StringComparison.Ordinal));
        Assert.Contains(spans, span => span is { Bold: true, Text: "bold" });
        Assert.Contains(spans, span => span is { Italic: true, Text: "italic" });
        Assert.Contains(spans, span => span is { Code: true, Text: "code" });
    }

    /// <summary>
    /// Verifies that an underscore inside an identifier is left alone, so a column name such as
    /// <c>is_subtotal</c> does not turn half the line italic.
    /// </summary>
    [Fact]
    public void Parse_UnderscoreInsideWord_IsNotEmphasis()
    {
        var spans = RichTextParser.ParseInline("The is_subtotal column");

        Assert.Equal("The is_subtotal column", string.Concat(spans.Select(span => span.Text)));
        Assert.DoesNotContain(spans, span => span.Italic);
    }

    /// <summary>
    /// Verifies that an escaped marker is shown literally.
    /// </summary>
    [Fact]
    public void Parse_EscapedMarker_IsLiteral()
    {
        var spans = RichTextParser.ParseInline(@"5 \* 3 = 15");

        Assert.Equal("5 * 3 = 15", string.Concat(spans.Select(span => span.Text)));
    }

    /// <summary>
    /// Verifies that a link keeps its visible text and records its target.
    /// </summary>
    [Fact]
    public void Parse_Link_KeepsTextAndTarget()
    {
        var spans = RichTextParser.ParseInline("See [the report](https://example.com/report) for detail.");

        var link = Assert.Single(spans, span => !string.IsNullOrEmpty(span.Link));

        Assert.Equal("the report", link.Text);
        Assert.Equal("https://example.com/report", link.Link);
    }

    /// <summary>
    /// Verifies that a pipe table becomes a table rather than lines of pipes.
    /// </summary>
    [Fact]
    public void Parse_PipeTable_BecomesTable()
    {
        var blocks = RichTextParser.Parse(
            """
            | Region | Amount |
            | --- | ---: |
            | North | 1,000 |
            | South | 2,000 |
            """);

        var block = Assert.Single(blocks);

        Assert.Equal(RichTextBlockKind.Table, block.Kind);
        Assert.Equal(2, block.Table.ColumnCount);
        Assert.Equal("Region", Flatten(block.Table.Header.Cells[0].Spans));
        Assert.Equal(2, block.Table.Rows.Count);
        Assert.Equal("South", Flatten(block.Table.Rows[1].Cells[0].Spans));
    }

    /// <summary>
    /// Verifies that a sentence containing a pipe is not mistaken for a table, since only a separator
    /// row makes one.
    /// </summary>
    [Fact]
    public void Parse_SentenceWithPipe_IsNotATable()
    {
        var block = Assert.Single(RichTextParser.Parse("Use the a | b syntax here."));

        Assert.Equal(RichTextBlockKind.Paragraph, block.Kind);
    }

    /// <summary>
    /// Verifies that a fenced code block is kept verbatim, including its indentation.
    /// </summary>
    [Fact]
    public void Parse_FencedCode_IsKeptVerbatim()
    {
        var blocks = RichTextParser.Parse("Before\n\n```sql\nSELECT *\n  FROM sales\n```\n\nAfter");

        var code = blocks.Single(block => block.Kind == RichTextBlockKind.Code);

        Assert.Equal("SELECT *\n  FROM sales", code.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that rules and quotes are recognized.
    /// </summary>
    [Fact]
    public void Parse_RuleAndQuote_AreRecognized()
    {
        var blocks = RichTextParser.Parse("---\n\n> A quoted line");

        Assert.Equal(RichTextBlockKind.HorizontalRule, blocks[0].Kind);
        Assert.Equal(RichTextBlockKind.Quote, blocks[1].Kind);
        Assert.Equal("A quoted line", Flatten(blocks[1]));
    }

    /// <summary>
    /// The bug this work exists for: an HTML answer must become structure, never literal tags.
    /// </summary>
    [Fact]
    public void Parse_HtmlDocument_BecomesStructureNotTags()
    {
        var blocks = RichTextParser.Parse(
            """
            <html><head><style>.x{color:red}</style></head>
            <body>
              <h1>Quarterly Report</h1>
              <p>Revenue rose by <strong>12%</strong> this quarter.</p>
              <ul><li>North grew</li><li>South held flat</li></ul>
            </body></html>
            """);

        var text = RichTextParser.ToPlainText(blocks);

        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.DoesNotContain(">", text, StringComparison.Ordinal);

        // The stylesheet body must not leak in as visible text once its tags are gone.
        Assert.DoesNotContain("color:red", text, StringComparison.Ordinal);

        Assert.Contains(blocks, block => block.Kind == RichTextBlockKind.Heading && Flatten(block) == "Quarterly Report");
        Assert.Contains(blocks, block => block.Kind == RichTextBlockKind.BulletItem && Flatten(block) == "North grew");
        Assert.Contains(blocks, block => block.Spans.Any(span => span is { Bold: true, Text: "12%" }));
    }

    /// <summary>
    /// Verifies that an HTML table becomes a real table rather than a run-on line of cell text.
    /// </summary>
    [Fact]
    public void Parse_HtmlTable_BecomesTable()
    {
        var blocks = RichTextParser.Parse(
            """
            <table>
              <tr><th>Region</th><th>Amount</th></tr>
              <tr><td>North</td><td>1,000</td></tr>
            </table>
            """);

        var block = Assert.Single(blocks, candidate => candidate.Kind == RichTextBlockKind.Table);

        Assert.Equal(2, block.Table.ColumnCount);
        Assert.Equal("Region", Flatten(block.Table.Header.Cells[0].Spans));
        Assert.Equal("North", Flatten(Assert.Single(block.Table.Rows).Cells[0].Spans));
    }

    /// <summary>
    /// Verifies that HTML entities are decoded, so the reader sees the character rather than its code.
    /// </summary>
    [Fact]
    public void Parse_HtmlEntities_AreDecoded()
    {
        var blocks = RichTextParser.Parse("<p>Profit &amp; loss &mdash; up 5&#37;</p>");

        var text = RichTextParser.ToPlainText(blocks);

        Assert.Contains("Profit & loss", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&#37;", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that ordinary prose containing a comparison is not mistaken for markup and stripped.
    /// </summary>
    [Fact]
    public void Parse_ProseWithComparison_IsNotTreatedAsHtml()
    {
        const string Text = "Use it when a < b and c > d.";

        Assert.False(HtmlToMarkupConverter.LooksLikeHtml(Text));
        Assert.Equal(Text, Flatten(Assert.Single(RichTextParser.Parse(Text))));
    }

    /// <summary>
    /// Verifies that empty input produces no blocks rather than a stray empty paragraph.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Parse_EmptyInput_ProducesNoBlocks(string text)
    {
        Assert.Empty(RichTextParser.Parse(text));
    }

    private static string Flatten(RichTextBlock block)
    {
        return Flatten(block.Spans);
    }

    private static string Flatten(IEnumerable<RichTextSpan> spans)
    {
        return string.Concat(spans.Select(span => span.Text));
    }
}
