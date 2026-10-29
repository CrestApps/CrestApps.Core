namespace CrestApps.Core.AI.Speech;

/// <summary>
/// A recording of the voice to clone.
/// </summary>
public sealed class VoiceCloneSample
{
    /// <summary>
    /// Gets or sets the file name, which tells the provider the audio's container (for example <c>sample.mp3</c>).
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the media type of the audio (for example <c>audio/mpeg</c>).
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the audio.
    /// </summary>
    public byte[] Content { get; set; }
}
