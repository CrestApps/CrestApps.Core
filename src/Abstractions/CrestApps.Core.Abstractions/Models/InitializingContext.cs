using System.Text.Json.Nodes;

namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.InitializingAsync"/> while
/// a catalog entry is being seeded with initial values from optional JSON data.
/// </summary>
public sealed class InitializingContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Gets the JSON data used to seed the entry with initial values.
    /// Defaults to an empty <see cref="JsonObject"/> when no data was provided.
    /// </summary>
    public JsonNode Data { get; }

    public InitializingContext(
        T model,
        JsonNode data)
    : base(model)
    {
        Data = data ?? new JsonObject();
    }
}
