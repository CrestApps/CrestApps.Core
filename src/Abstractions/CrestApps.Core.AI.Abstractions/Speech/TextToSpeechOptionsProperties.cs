namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Keys of <c>TextToSpeechOptions.AdditionalProperties</c> that a text-to-speech client may act on, for what the
/// options have no field for.
/// </summary>
public static class TextToSpeechOptionsProperties
{
    /// <summary>
    /// How the text should be spoken, in words (a <see cref="string"/>), for a model that takes instructions, such as
    /// OpenAI's <c>gpt-4o-mini-tts</c>. A client whose model takes none ignores it.
    /// </summary>
    public const string Instructions = "instructions";
}
