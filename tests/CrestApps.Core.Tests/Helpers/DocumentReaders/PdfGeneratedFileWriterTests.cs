using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Services;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class PdfGeneratedFileWriterTests
{
    private readonly PdfGeneratedFileWriter _writer = new();

    /// <summary>
    /// The regression this fix exists for: an HTML answer used to be written out one line per
    /// paragraph, putting raw tags on the page. Nothing resembling markup may survive into the PDF.
    /// </summary>
    [Fact]
    public async Task WriteAsync_HtmlBody_RendersNoMarkup()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Title = "Quarterly Report",
            Text =
                """
                <html><head><style>body{font-family:sans-serif}</style></head>
                <body>
                  <h1>Summary</h1>
                  <p>Revenue rose by <strong>12%</strong> against plan.</p>
                  <ul><li>North grew</li><li>South held flat</li></ul>
                  <table>
                    <tr><th>Region</th><th>Amount</th></tr>
                    <tr><td>North</td><td>1,000</td></tr>
                  </table>
                </body></html>
                """,
        });

        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.DoesNotContain(">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&nbsp;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family", text, StringComparison.Ordinal);

        // The content itself must survive the conversion, not just the tags being gone.
        Assert.Contains("Summary", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
        Assert.Contains("North grew", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Markdown emphasis and headings are rendered rather than shown as their markers.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MarkdownBody_RendersNoMarkers()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Text =
                """
                # Overview

                Revenue rose by **12%** against *plan*.

                - North grew
                - South held flat

                ---

                > Figures are provisional.
                """,
        });

        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", text, StringComparison.Ordinal);
        Assert.Contains("Overview", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
        Assert.Contains("Figures are provisional.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a Markdown table reaches the page with its values intact.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MarkdownTable_RendersValues()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Text =
                """
                | Region | Amount |
                | --- | --- |
                | North | 1,000 |
                | South | 2,000 |
                """,
        });

        Assert.DoesNotContain("---", text, StringComparison.Ordinal);
        Assert.Contains("Region", text, StringComparison.Ordinal);
        Assert.Contains("2,000", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that structured tabular data still renders, which is the path a tabular export uses.
    /// </summary>
    [Fact]
    public async Task WriteAsync_TabularData_RendersTable()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Header = ["Region", "Amount"],
            Rows = [["North", "1000"], ["South", "2000"]],
        });

        Assert.Contains("Region", text, StringComparison.Ordinal);
        Assert.Contains("South", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that plain prose is unchanged by the parsing, so ordinary answers are unaffected.
    /// </summary>
    [Fact]
    public async Task WriteAsync_PlainProse_IsPreserved()
    {
        const string Sentence = "The projection covers six sites and forty campaigns.";

        var text = await RenderTextAsync(new GeneratedFileContent { Text = Sentence });

        Assert.Contains(Sentence, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that empty content still produces a readable PDF rather than failing.
    /// </summary>
    [Fact]
    public async Task WriteAsync_EmptyContent_ProducesValidPdf()
    {
        await using var buffer = new MemoryStream();

        await _writer.WriteAsync(new GeneratedFileContent(), buffer, TestContext.Current.CancellationToken);

        Assert.True(buffer.Length > 0);

        buffer.Position = 0;
        using var document = PdfDocument.Open(buffer);

        Assert.Equal(1, document.NumberOfPages);
    }

    private async Task<string> RenderTextAsync(GeneratedFileContent content)
    {
        await using var buffer = new MemoryStream();

        await _writer.WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            // Page.Text concatenates glyphs with no word spacing, so it reports "Northgrew" for text
            // that renders correctly. The extracted words are what the reader actually sees.
            builder.AppendLine(string.Join(' ', page.GetWords().Select(word => word.Text)));
        }

        return builder.ToString();
    }
}
