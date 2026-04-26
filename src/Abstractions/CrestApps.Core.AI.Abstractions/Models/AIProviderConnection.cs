using System.Text.Json.Serialization;
using CrestApps.Core.AI.Json;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a configured AI provider connection that associates a client implementation
/// with its credentials and settings used to reach an AI backend.
/// </summary>
[JsonConverter(typeof(AIProviderConnectionJsonConverter))]
public sealed class AIProviderConnection : SourceCatalogEntry, INameAwareModel, IDisplayTextAwareModel, ICloneable<AIProviderConnection>
{
    /// <summary>
    /// Gets or sets the unique technical name used to identify this connection in settings and deployments.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name for this connection.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the AI client implementation associated with this connection.
    /// This maps to a registered key in <c>AIOptions.Clients</c>.
    /// </summary>
    [JsonIgnore]
    public string ClientName
    {
        get => Source;
        set => Source = value;
    }

    /// <summary>
    /// Gets or sets the legacy provider name that maps to <see cref="ClientName"/>.
    /// </summary>
    [Obsolete("Use ClientName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string ProviderName
    {
        get => Source;
        set => Source = value;
    }

    /// <summary>
    /// Gets or sets the UTC timestamp when this connection was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this connection.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this connection.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Gets or sets whether this catalog item is read-only.
    /// Configuration-backed entries are read-only and cannot be modified or deleted through the UI.
    /// </summary>
    public bool IsReadOnly { get; set; }

    public AIProviderConnection Clone()
    {
        return new AIProviderConnection
        {
            ItemId = ItemId,
            Source = Source,
            Name = Name,
            DisplayText = DisplayText,
            IsReadOnly = IsReadOnly,
            CreatedUtc = CreatedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties.Clone(),
        };
    }
}
