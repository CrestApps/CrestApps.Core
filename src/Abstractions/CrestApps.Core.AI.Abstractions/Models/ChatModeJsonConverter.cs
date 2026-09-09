using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Reads <see cref="ChatMode"/> while tolerating values the enum no longer defines.
/// </summary>
/// <remarks>
/// <para>
/// Realtime used to be a chat mode. It is now expressed by selecting a realtime-capable deployment, because
/// the deployment already answers that question and a second stored answer could disagree with it.
/// </para>
/// <para>
/// This converter is what makes removing the member safe. Without it, a profile or site setting written as
/// <c>"Realtime"</c> would throw while deserializing and take the whole settings object down with it. An
/// unrecognized value reads as <see cref="ChatMode.TextInput"/>, which is the correct outcome: whether such
/// a profile is a voice conversation is now decided by its chat deployment.
/// </para>
/// </remarks>
public sealed class ChatModeJsonConverter : JsonConverter<ChatMode>
{
    /// <inheritdoc/>
    public override ChatMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Enum.TryParse<ChatMode>(reader.GetString(), ignoreCase: true, out var parsed)
                    ? parsed
                    : ChatMode.TextInput;

            case JsonTokenType.Number:
                return reader.TryGetInt32(out var value) && Enum.IsDefined(typeof(ChatMode), value)
                    ? (ChatMode)value
                    : ChatMode.TextInput;

            default:
                return ChatMode.TextInput;
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChatMode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}
