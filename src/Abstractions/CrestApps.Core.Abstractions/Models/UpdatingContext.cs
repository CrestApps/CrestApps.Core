using System.Text.Json.Nodes;

namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.UpdatingAsync"/> while
/// a catalog entry is being modified and before the changes are persisted.
/// </summary>
public sealed class UpdatingContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Gets the JSON data carrying the field changes to apply to the entry.
    /// Defaults to an empty <see cref="JsonObject"/> when no data was provided.
    /// </summary>
    public JsonNode Data { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatingContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="data">The data.</param>
    public UpdatingContext(
        T model,
        JsonNode data)
        : base(model)
    {
        Data = data ?? new JsonObject();
    }
}
