using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.AGUI;

/// <summary>
/// The JSON settings of the AG-UI wire format: the AG-UI SDK's type information first, then the
/// Microsoft.Extensions.AI types, with the property names and null handling AG-UI clients expect.
/// </summary>
public static class AGUIJson
{
    /// <summary>
    /// Gets the serializer options for AG-UI events and run input.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>
    /// Serializes an AG-UI event to a JSON element, the shape it has on the wire.
    /// </summary>
    /// <param name="evt">The event.</param>
    /// <returns>The event as JSON.</returns>
    public static JsonElement SerializeEvent(BaseEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return JsonSerializer.SerializeToElement(evt, SerializerOptions.GetTypeInfo(typeof(BaseEvent)));
    }

    /// <summary>
    /// Reads the AG-UI run input from JSON.
    /// </summary>
    /// <param name="json">The run input as JSON.</param>
    /// <returns>The run input.</returns>
    /// <exception cref="JsonException">The JSON is not a valid run input.</exception>
    public static RunAgentInput DeserializeInput(JsonElement json)
    {
        return (RunAgentInput)json.Deserialize(SerializerOptions.GetTypeInfo(typeof(RunAgentInput)))
            ?? throw new JsonException("The AG-UI run input is empty.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AGUIJsonUtilities.DefaultTypeInfoResolver,
                AIJsonUtilities.DefaultOptions.TypeInfoResolver),
        };

        AGUIJsonUtilities.RegisterInterruptContentTypes(options);
        options.MakeReadOnly();

        return options;
    }
}
