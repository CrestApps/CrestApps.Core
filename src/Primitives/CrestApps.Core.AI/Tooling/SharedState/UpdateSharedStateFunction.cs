using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling.SharedState;

/// <summary>
/// The tool the agent changes the state it shares with the user through, with JSON Patch operations. It is offered
/// only in a chat run whose client shares a state.
/// </summary>
public sealed class UpdateSharedStateFunction : AIFunction
{
    /// <summary>
    /// The name of the tool.
    /// </summary>
    public const string FunctionName = "update_shared_state";

    private static readonly JsonElement _schema = JsonElement.Parse("""
        {
          "type": "object",
          "properties": {
            "operations": {
              "type": "array",
              "description": "The JSON Patch (RFC 6902) operations to apply to the shared state, in order.",
              "items": {
                "type": "object",
                "properties": {
                  "op": { "type": "string", "enum": ["add", "replace", "remove"] },
                  "path": { "type": "string", "description": "A JSON Pointer, such as /title or /items/0." },
                  "value": { "description": "The new value, for add and replace." }
                },
                "required": ["op", "path"]
              }
            }
          },
          "required": ["operations"]
        }
        """);

    private readonly ChatSharedState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSharedStateFunction"/> class.
    /// </summary>
    /// <param name="state">The state the tool changes.</param>
    public UpdateSharedStateFunction(ChatSharedState state)
    {
        _state = state;
    }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public override string Name => FunctionName;

    /// <summary>
    /// Gets what the tool does, for the model.
    /// </summary>
    public override string Description => "Changes the JSON state you share with the user, with JSON Patch operations (add, replace, remove). The user sees the change on the page.";

    /// <summary>
    /// Gets the JSON schema of the arguments.
    /// </summary>
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Applies the operations to the shared state and tells the client.
    /// </summary>
    /// <param name="arguments">The arguments, with the operations.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What happened, for the model.</returns>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments is null || !arguments.TryGetValue("operations", out var value) || value is null)
        {
            return "No operations were given. Pass the JSON Patch operations in 'operations'.";
        }

        if (ToJsonNode(value) is not JsonArray operations || operations.Count == 0)
        {
            return "'operations' must be a non-empty array of JSON Patch operations.";
        }

        JsonObject updated;

        try
        {
            updated = JsonPatchApplier.Apply(_state.Value, operations);
        }
        catch (JsonPatchException ex)
        {
            return $"The shared state was not changed: {ex.Message}";
        }

        _state.Value = updated;

        if (_state.OnChangedAsync is not null)
        {
            await _state.OnChangedAsync((JsonArray)operations.DeepClone(), (JsonObject)updated.DeepClone(), cancellationToken);
        }

        return "The shared state was updated.";
    }

    private static JsonNode ToJsonNode(object value)
    {
        return value switch
        {
            JsonNode node => node,
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            string text => TryParse(text),
            _ => JsonSerializer.SerializeToNode(value, AIJsonUtilities.DefaultOptions),
        };
    }

    private static JsonNode TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
