using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Json;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a normalized provider connection payload used by AI client factories
/// and providers.
/// </summary>
[JsonConverter(typeof(AIProviderConnectionConverter))]
public sealed class AIProviderConnectionEntry : ReadOnlyDictionary<string, object>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIProviderConnectionEntry"/> class.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public AIProviderConnectionEntry(AIProviderConnectionEntry connection)
        : base(connection)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProviderConnectionEntry"/> class.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    public AIProviderConnectionEntry(IDictionary<string, object> dictionary)
        : base(dictionary)
    {
    }
}
