namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Which direction a caption sits relative to what it captions.
/// </summary>
public enum CaptionDirection
{
    /// <summary>
    /// Not known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The caption sits above.
    /// </summary>
    Above = 1,

    /// <summary>
    /// The caption sits below.
    /// </summary>
    Below = 2,
}
