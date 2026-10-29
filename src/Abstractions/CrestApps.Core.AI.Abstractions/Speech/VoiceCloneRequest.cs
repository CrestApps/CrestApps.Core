namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Describes a voice to create from recordings.
/// </summary>
public sealed class VoiceCloneRequest
{
    /// <summary>
    /// Gets or sets the name of the new voice.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a description of the voice, or <see langword="null"/>.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the language the recordings are in, as a BCP-47 tag, for a provider that asks for it.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the recordings of the voice. Providers differ in how many they take and how long they need them.
    /// </summary>
    public IReadOnlyList<VoiceCloneSample> Samples { get; set; } = [];
}
