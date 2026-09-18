using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A tool result that is more than prose and can say so.
/// </summary>
/// <remarks>
/// A search that found a chart has a picture to hand over, and flattening the result to <c>ToString()</c>
/// throws that away — the caller is told a figure exists and given no way to see it. A result that
/// implements this hands back the parts it is made of, and each transport renders them its own way: an MCP
/// server as content blocks, a chat turn as text.
/// <para>
/// The parts are <see cref="AIContent"/> rather than any one transport's types, so nothing here has to know
/// about a protocol and no assembly has to reference one to produce a picture.
/// </para>
/// </remarks>
public interface IAIToolContentProvider
{
    /// <summary>
    /// Renders the result as the parts it is made of.
    /// </summary>
    /// <returns>The contents.</returns>
    IReadOnlyList<AIContent> ToContents();
}
