namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// The files of a stored animation that can be served.
/// </summary>
public enum AnimationAsset
{
    /// <summary>
    /// The H.264 MP4 video.
    /// </summary>
    Video,

    /// <summary>
    /// The PNG still shown before the video plays.
    /// </summary>
    Poster,
}
