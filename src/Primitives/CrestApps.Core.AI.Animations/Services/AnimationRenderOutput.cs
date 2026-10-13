namespace CrestApps.Core.AI.Animations.Services;

/// <summary>
/// A rendered video waiting to be stored. Disposing it deletes the folder it was rendered in.
/// </summary>
internal sealed class AnimationRenderOutput : IDisposable
{
    private readonly string _workingFolder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationRenderOutput"/> class.
    /// </summary>
    /// <param name="workingFolder">The folder the render worked in.</param>
    /// <param name="videoPath">The rendered MP4.</param>
    /// <param name="poster">The PNG still shown before the video plays.</param>
    /// <param name="videoBytes">The size of the video in bytes.</param>
    public AnimationRenderOutput(
        string workingFolder,
        string videoPath,
        byte[] poster,
        long videoBytes)
    {
        _workingFolder = workingFolder;
        VideoPath = videoPath;
        Poster = poster;
        VideoBytes = videoBytes;
    }

    /// <summary>
    /// Gets the path of the rendered MP4.
    /// </summary>
    public string VideoPath { get; }

    /// <summary>
    /// Gets the PNG still shown before the video plays.
    /// </summary>
    public byte[] Poster { get; }

    /// <summary>
    /// Gets the size of the video in bytes.
    /// </summary>
    public long VideoBytes { get; }

    /// <summary>
    /// Deletes the folder the video was rendered in.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workingFolder))
            {
                Directory.Delete(_workingFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the stale-folder sweep of the next process that renders.
        }
    }
}
