namespace CrestApps.Core.AI.Models;

/// <summary>
/// Provides extension methods for <see cref="ResolvedAIPersona"/>.
/// </summary>
public static class AIPersonaExtensions
{
    /// <summary>
    /// Places the persona instructions at the top of a system message. The persona says who the assistant is;
    /// the prompt templates and the system message after it say what it does, so their rules win any conflict.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <param name="systemMessage">The system message composed from prompt templates and the resource's own text.</param>
    /// <returns>The combined system message, or <see langword="null"/> when both parts are empty.</returns>
    public static string PrependTo(this ResolvedAIPersona persona, string systemMessage)
    {
        if (string.IsNullOrWhiteSpace(persona?.Instructions))
        {
            return systemMessage;
        }

        if (string.IsNullOrWhiteSpace(systemMessage))
        {
            return persona.Instructions;
        }

        return persona.Instructions + "\n\n" + systemMessage;
    }
}
