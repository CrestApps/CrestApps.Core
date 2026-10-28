namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Keys of <c>SpeechToTextOptions.AdditionalProperties</c> that a speech-to-text client may act on, for what the
/// options have no field for.
/// </summary>
public static class SpeechToTextOptionsProperties
{
    /// <summary>
    /// Words and phrases the recognizer should favor (an <see cref="IReadOnlyList{T}"/> of <see cref="string"/>). A
    /// client whose provider cannot bias recognition ignores it.
    /// </summary>
    public const string BoostedKeywords = "boostedKeywords";

    /// <summary>
    /// The languages the recognizer should expect (an <see cref="IReadOnlyList{T}"/> of <see cref="string"/>), primary
    /// first, for a provider that can listen for several.
    /// </summary>
    public const string RecognitionLanguages = "recognitionLanguages";
}
