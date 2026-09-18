using ModelContextProtocol.Protocol;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// A tool result that knows how it should reach an MCP client.
/// </summary>
/// <remarks>
/// A tool result is not always prose. A search that found a chart has a picture to hand over, and flattening
/// it to <c>ToString()</c> throws that away — the client is told a figure exists and given no way to see it.
/// </remarks>
public interface IMcpToolContentProvider
{
    /// <summary>
    /// Renders the result as the content blocks to return to the client.
    /// </summary>
    /// <returns>The content blocks.</returns>
    IReadOnlyList<ContentBlock> ToContentBlocks();
}
