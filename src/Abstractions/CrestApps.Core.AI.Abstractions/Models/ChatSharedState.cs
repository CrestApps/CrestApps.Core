using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// The state the user and the agent share during a chat run: a JSON object the client sends with the message and
/// the agent changes with JSON Patch operations, so both sides work on the same thing.
/// </summary>
public sealed class ChatSharedState
{
    /// <summary>
    /// Gets or sets the current state.
    /// </summary>
    public JsonObject Value { get; set; } = [];

    /// <summary>
    /// Gets or sets the callback that runs after the agent changed the state, with the JSON Patch operations it
    /// applied and the new state. The chat run uses it to tell the client.
    /// </summary>
    public Func<JsonArray, JsonObject, CancellationToken, ValueTask> OnChangedAsync { get; set; }
}
