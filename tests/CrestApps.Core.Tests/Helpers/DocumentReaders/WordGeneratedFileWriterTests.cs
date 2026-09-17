using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class WordGeneratedFileWriterTests
{
    private readonly WordGeneratedFileWriter _writer = new();

    /// <summary>
    /// The same defect the PDF writer had: an HTML answer was written one literal line per paragraph,
    /// putting raw tags in front of the reader.
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
                </body></html>
                """,
        });

        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.DoesNotContain(">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family", text, StringComparison.Ordinal);

        Assert.Contains("Summary", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
        Assert.Contains("North grew", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Markdown markers are rendered rather than shown.
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
                """,
        });

        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", text, StringComparison.Ordinal);
        Assert.Contains("Overview", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a Markdown table becomes a real table rather than rows of pipes.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MarkdownTable_BecomesTable()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text =
                """
                | Region | Amount |
                | --- | --- |
                | North | 1,000 |
                | South | 2,000 |
                """,
        });

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());

        // One header row plus two data rows.
        Assert.Equal(3, table.Elements<TableRow>().Count());
    }

    /// <summary>
    /// Verifies that emphasis becomes real run formatting.
    /// </summary>
    [Fact]
    public async Task WriteAsync_BoldText_BecomesBoldRun()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text = "Revenue rose by **12%** against plan.",
        });

        var runs = document.MainDocumentPart.Document.Body.Descendants<Run>().ToList();
        var bold = runs.Where(run => run.RunProperties?.GetFirstChild<Bold>() is not null).ToList();

        Assert.Contains(bold, run => run.InnerText == "12%");
    }

    /// <summary>
    /// A document using every supported construct must be structurally valid, because an invalid part
    /// makes the word processor refuse the whole file.
    /// </summary>
    [Fact]
    public async Task WriteAsync_FullyFormattedDocument_IsValid()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Title = "Monthly Review",
            Text =
                """
                # Overview

                Revenue tracked **2.1% below** plan; see [the workbook](https://example.com/w) for detail.

                ## Actions

                1. Re-forecast the named accounts.
                2. Confirm headcount assumptions.

                - A bulleted note
                - ~~A withdrawn note~~

                | Account | Plan | Actual |
                | --- | --- | --- |
                | Account A | 1,000 | 1,200 |

                > Provisional until the close.

                ---

                ```sql
                SELECT account FROM projections
                ```
                """,
            Header = ["Region", "Amount"],
            Rows = [["North", "1000"]],
        });

        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document, TestContext.Current.CancellationToken).ToList();

        Assert.True(
            errors.Count == 0,
            "The generated document is not valid: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>
    /// Verifies that plain prose is unchanged, so ordinary answers are unaffected.
    /// </summary>
    [Fact]
    public async Task WriteAsync_PlainProse_IsPreserved()
    {
        const string Sentence = "The projection covers six sites and forty campaigns.";

        Assert.Contains(Sentence, await RenderTextAsync(new GeneratedFileContent { Text = Sentence }), StringComparison.Ordinal);
    }

    private async Task<WordprocessingDocument> WriteAsync(GeneratedFileContent content)
    {
        var buffer = new MemoryStream();

        await _writer.WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        buffer.Position = 0;

        return WordprocessingDocument.Open(buffer, isEditable: false);
    }

    private async Task<string> RenderTextAsync(GeneratedFileContent content)
    {
        using var document = await WriteAsync(content);
        var builder = new StringBuilder();

        foreach (var paragraph in document.MainDocumentPart.Document.Body.Descendants<Paragraph>())
        {
            builder.AppendLine(paragraph.InnerText);
        }

        return builder.ToString();
    }
}
