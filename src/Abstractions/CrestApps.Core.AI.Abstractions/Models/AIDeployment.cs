using System.Text.Json.Serialization;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a configured AI deployment that maps a technical name and capability type
/// to a specific AI model via a registered client and optional provider connection.
/// </summary>
public sealed class AIDeployment : SourceCatalogEntry, INameAwareModel, ISourceAwareModel, IModifiedUtcAwareModel, ICloneable<AIDeployment>
{
    private string _modelName;
    private AIDeploymentCapability _capability;

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
    /// Gets or sets the capabilities of this deployment (Chat, Utility, Embedding, Image, SpeechToText, TextToSpeech, Vision).
    /// A deployment can support one or more capabilities.
    /// </summary>
    public AIDeploymentCapability Capability
    {
        get => _capability;
        set => _capability = value;
    }

    /// <summary>
    /// Gets or sets the legacy deployment type flags.
    /// Use <see cref="Capability"/> for new code.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
    [Obsolete("Use Capability instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public AIDeploymentType Type
    {
        get => Capability.ToLegacyType();
        set => Capability = value.ToCapability();
    }

    [JsonInclude]
    [JsonPropertyName("Type")]
    private AIDeploymentType LegacyType
    {
        set => Capability = value.ToCapability();
    }
#pragma warning restore CS0618 // Type or member is obsolete

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
    /// Determines whether the deployment supports the specified legacy type.
    /// </summary>
    /// <param name="type">The type.</param>
#pragma warning disable CS0618 // Type or member is obsolete
    public bool SupportsType(AIDeploymentType type)
    {
        return Capability.Supports(type.ToCapability());
    }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Determines whether the deployment supports the specified capability.
    /// </summary>
    /// <param name="capability">The capability.</param>
    public bool SupportsCapability(AIDeploymentCapability capability)
    {
        return Capability.Supports(capability);
    }

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
            Capability = Capability,
            IsReadOnly = IsReadOnly,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties.Clone(),
        };
    }
}
