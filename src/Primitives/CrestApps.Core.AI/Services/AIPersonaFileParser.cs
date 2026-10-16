using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Turns a persona file into an <see cref="AIPersona"/>. The front matter holds the persona's fields and the body
/// holds its instructions:
/// <code>
/// ---
/// Title: Friendly assistant
/// AssistantName: Ava
/// Language: en-US
/// ---
/// You are Ava, a warm and patient assistant.
/// </code>
/// </summary>
public static class AIPersonaFileParser
{
    /// <summary>
    /// Parses a persona file.
    /// </summary>
    /// <param name="id">The persona's technical name, taken from the file path.</param>
    /// <param name="parseResult">The parsed front matter and body.</param>
    public static AIPersona Parse(string id, TemplateParseResult parseResult)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(parseResult);

        var metadata = parseResult.Metadata;
        var props = metadata?.AdditionalProperties;

        var persona = new AIPersona
        {
            ItemId = id,
            Name = id,
            DisplayText = metadata?.Title ?? id.Replace('-', ' ').Replace('.', ' '),
            Description = metadata?.Description,
            Instructions = NullIfWhiteSpace(parseResult.Body),
        };

        if (props is null)
        {
            return persona;
        }

        if (props.TryGetValue(nameof(AIPersona.AssistantName), out var assistantName))
        {
            persona.AssistantName = NullIfWhiteSpace(assistantName);
        }

        if (props.TryGetValue(nameof(AIPersona.Language), out var language))
        {
            persona.Language = NullIfWhiteSpace(language);
        }

        if (props.TryGetValue(nameof(AIPersona.VoiceName), out var voiceName))
        {
            persona.VoiceName = NullIfWhiteSpace(voiceName);
        }

        if (props.TryGetValue(nameof(AIPersona.VoiceDeploymentName), out var voiceDeploymentName))
        {
            persona.VoiceDeploymentName = NullIfWhiteSpace(voiceDeploymentName);
        }

        if (props.TryGetValue(nameof(AIPersona.AvatarUrl), out var avatarUrl))
        {
            persona.AvatarUrl = NullIfWhiteSpace(avatarUrl);
        }

        return persona;
    }

    private static string NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
