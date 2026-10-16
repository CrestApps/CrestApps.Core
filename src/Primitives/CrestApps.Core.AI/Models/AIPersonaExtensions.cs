namespace CrestApps.Core.AI.Models;

/// <summary>
/// Provides extension methods for <see cref="ResolvedAIPersona"/>.
/// </summary>
public static class AIPersonaExtensions
{
    /// <summary>
    /// Places the persona block at the top of a system message. The block names the assistant, when the persona gives
    /// it a name, then holds the persona instructions. The persona says who the assistant is; the prompt templates and
    /// the system message after it say what it does, so their rules win any conflict.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <param name="systemMessage">The system message composed from prompt templates and the resource's own text.</param>
    /// <returns>The combined system message, or <see langword="null"/> when both parts are empty.</returns>
    public static string PrependTo(this ResolvedAIPersona persona, string systemMessage)
    {
        var block = persona.GetPersonaBlock();

        if (block is null)
        {
            return systemMessage;
        }

        if (string.IsNullOrWhiteSpace(systemMessage))
        {
            return block;
        }

        return block + "\n\n" + systemMessage;
    }

    /// <summary>
    /// Builds the persona block that leads the system message: "Your name is X." when the persona names the
    /// assistant, so the model knows its name even when the instructions do not say it, then the instructions.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <returns>The persona block, or <see langword="null"/> when the persona has neither a name nor instructions.</returns>
    public static string GetPersonaBlock(this ResolvedAIPersona persona)
    {
        if (persona is null)
        {
            return null;
        }

        var hasName = !string.IsNullOrWhiteSpace(persona.AssistantName);
        var hasInstructions = !string.IsNullOrWhiteSpace(persona.Instructions);

        if (!hasName)
        {
            return hasInstructions ? persona.Instructions : null;
        }

        var nameLine = $"Your name is {persona.AssistantName.Trim()}.";

        return hasInstructions
            ? nameLine + "\n" + persona.Instructions
            : nameLine;
    }

    /// <summary>
    /// Maps a persona to what a chat surface shows on assistant messages: the assistant name and avatar.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <returns>The display values; <see cref="AIChatAssistantDisplay.Default"/> when there is no persona.</returns>
    public static AIChatAssistantDisplay ToChatAssistantDisplay(this ResolvedAIPersona persona)
    {
        if (persona is null)
        {
            return AIChatAssistantDisplay.Default;
        }

        return new AIChatAssistantDisplay
        {
            AssistantName = NullIfWhiteSpace(persona.AssistantName),
            AvatarUrl = NullIfWhiteSpace(persona.AvatarUrl),
        };
    }

    private static string NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
