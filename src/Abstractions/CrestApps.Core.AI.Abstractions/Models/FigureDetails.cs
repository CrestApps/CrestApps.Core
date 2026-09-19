namespace CrestApps.Core.AI.Models;

/// <summary>
/// The detail a figure carries beyond the fields every knowledge object has.
/// </summary>
public sealed class FigureDetails
{
    /// <summary>
    /// Gets or sets the caption printed with the figure.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets how the caption was arrived at, so a reader can tell a printed caption from an inference.
    /// </summary>
    public string CaptionSource { get; set; }

    /// <summary>
    /// Gets or sets the surrounding body text that gives the figure its meaning.
    /// </summary>
    public string Context { get; set; }

    /// <summary>
    /// Gets or sets what the figure was judged to be worth: skipped, kept for its caption, or transcribed.
    /// </summary>
    public string Tier { get; set; }

    /// <summary>
    /// Gets or sets the transcription of the figure.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the deployment that produced the transcription.
    /// </summary>
    public string DescriptionModel { get; set; }

    /// <summary>
    /// Gets or sets the prompt version the transcription was produced with, so a prompt change is not hidden
    /// behind a stale description.
    /// </summary>
    public string DescriptionPromptVersion { get; set; }

    /// <summary>
    /// Gets or sets why transcription failed, when it did.
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Gets or sets the figure width, in image samples.
    /// </summary>
    public int? PixelWidth { get; set; }

    /// <summary>
    /// Gets or sets the figure height, in image samples.
    /// </summary>
    public int? PixelHeight { get; set; }
}
