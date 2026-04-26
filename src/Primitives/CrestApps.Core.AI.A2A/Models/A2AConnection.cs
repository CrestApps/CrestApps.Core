using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.A2A.Models;

/// <summary>
/// Represents the A 2 A Connection.
/// </summary>
public sealed class A2AConnection : CatalogItem, IDisplayTextAwareModel, ICloneable<A2AConnection>
{
    /// <summary>
    /// Gets or sets the display Text.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public string Endpoint { get; set; }

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

    public A2AConnection Clone()
    {
        return new A2AConnection()
        {
            ItemId = ItemId,
            DisplayText = DisplayText,
            Endpoint = Endpoint,
            CreatedUtc = CreatedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties,
        };
    }
}
