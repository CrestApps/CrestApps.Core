namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// A rendered animation, ready to be stored.
/// </summary>
public sealed class AnimationContent
{
    /// <summary>
    /// Gets or sets the path of the rendered MP4. The store moves the file rather than copying it.
    /// </summary>
    public string VideoFilePath { get; set; }

    /// <summary>
    /// Gets or sets the PNG still shown before the video plays.
    /// </summary>
    public ReadOnlyMemory<byte> Poster { get; set; }

    /// <summary>
    /// Gets or sets the arguments the video was requested with — its title, length, size, seed and look — as JSON.
    /// </summary>
    public string SettingsJson { get; set; }

    /// <summary>
    /// Gets or sets the script the video was drawn with, so a follow-up can change it rather than start again.
    /// </summary>
    public string Script { get; set; }
}
