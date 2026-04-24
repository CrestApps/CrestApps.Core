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
    public AIProviderConnectionEntry(AIProviderConnectionEntry connection)
        : base(connection)
    {
    }

    public AIProviderConnectionEntry(IDictionary<string, object> dictionary)
        : base(dictionary)
    {
    }
}
