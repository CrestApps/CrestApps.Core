using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestApps.Core;

/// <summary>
/// Base class for entities that support dynamic extensible properties.
/// </summary>
public abstract class ExtensibleEntity
{
    /// <summary>
    /// Gets or sets the dictionary of additional properties stored under the
    /// <c>Properties</c> JSON object.
    /// </summary>
    [JsonConverter(typeof(ExtensibleEntityPropertiesJsonConverter))]
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
}
