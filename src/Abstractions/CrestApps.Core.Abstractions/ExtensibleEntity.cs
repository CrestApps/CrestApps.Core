using System.Text.Json.Serialization;

namespace CrestApps.Core;

/// <summary>
/// Base class for entities that support dynamic extensible properties.
/// </summary>
public abstract class ExtensibleEntity
{
    /// <summary>
    /// Gets or sets the dictionary of additional properties that are not explicitly
    /// declared on the entity. Values are captured from JSON extension data during
    /// deserialization and are round-tripped back on serialization.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
