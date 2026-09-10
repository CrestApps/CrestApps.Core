using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a configured AI deployment that maps a technical name and deployment purpose
/// to a specific AI model via a registered client and optional provider connection.
/// </summary>
public sealed class AIDeployment : SourceCatalogEntry, INameAwareModel, ISourceAwareModel, IModifiedUtcAwareModel, ICloneable<AIDeployment>, IJsonOnDeserialized
{
    private string _modelName;
    private string[] _legacyPurposes;
    private int _legacyPurposeRank;

    /// <summary>
    /// Gets or sets the technical name of the AI client implementation to use for this deployment.
    /// This maps to a registered key in <c>AIOptions.Clients</c>.
    /// For connection-based deployments, this is typically derived from the connection's <c>ClientName</c>.
    /// </summary>
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
    public string ProviderName { get => Source; set => Source = value; }

    [JsonInclude]
    [JsonPropertyName("ProviderName")]
    private string _providerNameBackingField { set => Source = value; }

    /// <summary>
    /// Gets or sets the unique technical name used to identify this deployment in settings, profiles, and recipes.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the provider-facing model or deployment name.
    /// Falls back to <see cref="Name"/> for backward compatibility with legacy records.
    /// </summary>
    public string ModelName
    {
        get => string.IsNullOrWhiteSpace(_modelName)
            ? Name
            : _modelName;
        set => _modelName = value?.Trim();
    }

    /// <summary>
    /// Gets or sets the name of the provider connection this deployment is associated with.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// Gets the legacy purpose names captured while this record was deserialized, if it carried any.
    /// </summary>
    /// <remarks>
    /// A deployment no longer has a purpose — it declares capabilities. Records written before that change
    /// still carry a <c>Purpose</c>, <c>Capability</c>, or <c>Type</c> field, and the value is captured here
    /// so <see cref="AIDeploymentPurposeCompatibility"/> can project it onto capabilities. This is the
    /// upgrade path for stored data and is deliberately not part of the public surface.
    /// </remarks>
    [JsonIgnore]
    internal IReadOnlyList<string> LegacyPurposes => _legacyPurposes;

    /// <summary>
    /// Forgets the captured legacy purpose, once it has been projected onto capabilities.
    /// </summary>
    /// <remarks>
    /// The projection is a one-time translation of a record written before capabilities existed, not a
    /// standing rule. Holding on to the purpose after it has been applied would let it re-apply on a later
    /// update and restore a capability the operator had just removed.
    /// </remarks>
    internal void ClearLegacyPurposes()
    {
        _legacyPurposes = null;
        _legacyPurposeRank = 0;
    }

    [JsonInclude]
    [JsonPropertyName("Purpose")]
    private JsonElement LegacyPurpose { set => CaptureLegacyPurpose(value, rank: 1); }

    [JsonInclude]
    [JsonPropertyName("Capability")]
    private JsonElement LegacyCapability { set => CaptureLegacyPurpose(value, rank: 2); }

    [JsonInclude]
    [JsonPropertyName("Type")]
    private JsonElement LegacyType { set => CaptureLegacyPurpose(value, rank: 3); }

    /// <summary>
    /// Records a legacy purpose value, keeping the highest-precedence field when a record carries more than
    /// one of them. JSON property order is not guaranteed, so precedence is applied by rank rather than by
    /// arrival: <c>Purpose</c> outranks <c>Capability</c>, which outranks the oldest name, <c>Type</c>.
    /// </summary>
    private void CaptureLegacyPurpose(JsonElement value, int rank)
    {
        if (_legacyPurposes is not null && rank >= _legacyPurposeRank)
        {
            return;
        }

        string[] names = value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()],
            JsonValueKind.Number => [value.ToString()],
            JsonValueKind.Array => [.. value.EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))],
            _ => null,
        };

        if (names is not { Length: > 0 })
        {
            return;
        }

        _legacyPurposes = names;
        _legacyPurposeRank = rank;
    }

    /// <summary>
    /// Projects any legacy purpose this record carried onto the model capability features, after the record
    /// has been read from the store.
    /// </summary>
    /// <remarks>
    /// This is the store deserialization half of the read-time normalization described on
    /// <see cref="AIDeploymentPurposeCompatibility"/>; the JSON-node half lives in the deployment catalog
    /// handler. It runs from <see cref="IJsonOnDeserialized"/> rather than from a property setter because the
    /// rule needs both the legacy purpose and the already-declared features, and JSON property order is not
    /// guaranteed.
    /// </remarks>
    void IJsonOnDeserialized.OnDeserialized()
    {
        AIDeploymentPurposeCompatibility.Normalize(this);
    }

    /// <summary>
    /// Gets or sets the UTC timestamp when this deployment was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this deployment was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this deployment.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this deployment.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Gets or sets whether this catalog item is read-only.
    /// Configuration-backed entries are read-only and cannot be modified or deleted through the UI.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Clones the operation.
    /// </summary>
    public AIDeployment Clone()
    {
        return new AIDeployment
        {
            ItemId = ItemId,
            Name = Name,
            ModelName = _modelName,
            Source = Source,
            ConnectionName = ConnectionName,
            _legacyPurposes = _legacyPurposes,
            _legacyPurposeRank = _legacyPurposeRank,
            IsReadOnly = IsReadOnly,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties.Clone(),
        };
    }
}
