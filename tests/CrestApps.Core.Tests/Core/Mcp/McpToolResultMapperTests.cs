using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Tests.Core.Mcp;

/// <summary>
/// Covers what a tool result becomes on the wire. Flattening every result to <c>ToString()</c> reduced a
/// search that found a chart to a sentence saying a chart exists, with no way for the client to see it.
/// </summary>
public sealed class McpToolResultMapperTests
{
    /// <summary>
    /// Verifies that a plain string result is still exactly one text block. This is the shape every existing
    /// tool produces and it must not change.
    /// </summary>
    [Fact]
    public void ToCallToolResult_String_YieldsOneTextBlock()
    {
        var result = McpToolResultMapper.ToCallToolResult("the answer");

        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));

        Assert.Equal("the answer", block.Text);
    }

    /// <summary>
    /// Verifies that nothing at all is still one block, because a call result with no content is not valid.
    /// </summary>
    [Fact]
    public void ToCallToolResult_Null_YieldsOneEmptyTextBlock()
    {
        var result = McpToolResultMapper.ToCallToolResult(null);

        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));

        Assert.Equal(string.Empty, block.Text);
    }

    /// <summary>
    /// Verifies that a search that found a figure hands the client the text and a link to the picture.
    /// </summary>
    [Fact]
    public void ToCallToolResult_RetrievalResultWithFigure_YieldsTextAndResourceLink()
    {
        var retrieval = new DataSourceRetrievalResult
        {
            Text = "Relevant content from data source:",
            Figures =
            [
                new RetrievedFigure
                {
                    Id = "figure:key:1:0",
                    Label = "[fig:1]",
                    Title = "Figure 1. The measurements.",
                    Caption = "Figure 1. The measurements.",
                    Uri = "crestapps://datasource/data-source-1/figure/figure:key:1:0",
                    MediaType = "image/png",
                    Page = 8,
                },
            ],
        };

        var result = McpToolResultMapper.ToCallToolResult(retrieval);

        Assert.Equal(2, result.Content.Count);

        var text = Assert.IsType<TextContentBlock>(result.Content[0]);

        Assert.Equal("Relevant content from data source:", text.Text);

        var link = Assert.IsType<ResourceLinkBlock>(result.Content[1]);

        Assert.Equal("crestapps://datasource/data-source-1/figure/figure:key:1:0", link.Uri);
        Assert.Equal("Figure 1. The measurements.", link.Name);
        Assert.Equal("image/png", link.MimeType);
    }

    /// <summary>
    /// Verifies that a result carrying image bytes reaches the client as an image, not as a description of
    /// one.
    /// </summary>
    [Fact]
    public void ToCallToolResult_ImageContent_YieldsImageBlock()
    {
        var contents = new AIContent[]
        {
            new TextContent("figure: figure:key:1:0"),
            new DataContent(new byte[] { 1, 2, 3, 4 }, "image/png"),
        };

        var result = McpToolResultMapper.ToCallToolResult(contents);

        Assert.Equal(2, result.Content.Count);

        var image = Assert.IsType<ImageContentBlock>(result.Content[1]);

        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([1, 2, 3, 4], image.Data.ToArray());
    }

    /// <summary>
    /// Verifies that a result that is neither prose nor content still renders, so no tool can produce an
    /// empty call result by returning something unexpected.
    /// </summary>
    [Fact]
    public void ToCallToolResult_UnknownType_FallsBackToToString()
    {
        var result = McpToolResultMapper.ToCallToolResult(42);

        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));

        Assert.Equal("42", block.Text);
    }
}
