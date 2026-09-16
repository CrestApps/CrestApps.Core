using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Tooling;

/// <summary>
/// Covers what a chat model reads when a tool returns something richer than a string. A function-invoking
/// client serializes anything else as JSON, which would hand the model an envelope around its text - or a
/// picture as a base64 string many kilobytes long - instead of the text the result renders itself as.
/// </summary>
public sealed class AIToolResultTextTests
{
    /// <summary>
    /// Verifies that a string and nothing at all pass through untouched, so every existing tool behaves
    /// exactly as it did.
    /// </summary>
    [Fact]
    public void Normalize_StringOrNull_IsReturnedUnchanged()
    {
        Assert.Equal("plain", AIToolResultText.Normalize("plain"));
        Assert.Null(AIToolResultText.Normalize(null));
    }

    /// <summary>
    /// Verifies that a result that renders itself reaches the model as its text rather than as JSON.
    /// </summary>
    [Fact]
    public void Normalize_ContentProvider_BecomesItsText()
    {
        var result = new DataSourceRetrievalResult
        {
            Text = "Relevant content.",
            Figures =
            [
                new RetrievedFigure
                {
                    Id = "figure:key:1:0",
                    Uri = "crestapps://datasource/ds/figure/figure:key:1:0",
                },
            ],
        };

        var normalized = Assert.IsType<string>(AIToolResultText.Normalize(result));

        Assert.Equal("Relevant content.", normalized);
    }

    /// <summary>
    /// Verifies that a set of contents collapses to its text parts, and that a picture among them is not sent
    /// to the model as bytes.
    /// </summary>
    [Fact]
    public void Normalize_AIContents_JoinsTheTextParts()
    {
        var contents = new List<AIContent>
        {
            new TextContent("First."),
            new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
            new TextContent("Second."),
        };

        Assert.Equal("First.\nSecond.", AIToolResultText.Normalize(contents));
    }

    /// <summary>
    /// Verifies that a plain object a tool meant to serialize is left alone.
    /// </summary>
    [Fact]
    public void Normalize_PlainObject_IsReturnedUnchanged()
    {
        var value = new { count = 3 };

        Assert.Same(value, AIToolResultText.Normalize(value));
    }
}
