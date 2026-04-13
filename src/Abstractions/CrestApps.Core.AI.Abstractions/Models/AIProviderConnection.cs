using System.Text.Json.Serialization;
using CrestApps.Core.AI.Json;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

[JsonConverter(typeof(AIProviderConnectionJsonConverter))]
public sealed class AIProviderConnection : SourceCatalogEntry, INameAwareModel, IDisplayTextAwareModel, ICloneable<AIProviderConnection>
{
    public string Name { get; set; }

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

    [Obsolete("Use ClientName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string ProviderName
    {
        get => Source;
        set => Source = value;
    }

    public DateTime CreatedUtc { get; set; }

    public string Author { get; set; }

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
