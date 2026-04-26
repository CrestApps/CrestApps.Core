using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the MCP Connection.
/// </summary>
public sealed class McpConnection : SourceCatalogEntry, IDisplayTextAwareModel, ICloneable<McpConnection>
{
    /// <summary>
    /// Gets or sets the display Text.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the created Utc.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner ID.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Clones the operation.
    /// </summary>
    public McpConnection Clone()
    {
        return new McpConnection()
        {
            ItemId = ItemId,
            Source = Source,
            DisplayText = DisplayText,
            CreatedUtc = CreatedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties,
        };
    }
}
