namespace CrestApps.Core.AI.Models;

/// <summary>
/// Indicates the gender of a speech synthesis voice.
/// </summary>
public enum SpeechVoiceGender
{
    /// <summary>
    /// The voice gender is not specified or cannot be determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// A male voice.
    /// </summary>
    Male,

    /// <summary>
    /// A female voice.
    /// </summary>
    Female,

    /// <summary>
    /// A gender-neutral voice.
    /// </summary>
    Neutral,
}
