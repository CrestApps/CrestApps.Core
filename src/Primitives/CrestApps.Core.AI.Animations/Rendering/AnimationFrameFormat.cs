namespace CrestApps.Core.AI.Animations.Rendering;

/// <summary>
/// The size, rate and length of a video's frames.
/// </summary>
/// <param name="Width">The frame width in pixels.</param>
/// <param name="Height">The frame height in pixels.</param>
/// <param name="FrameRate">The frames per second.</param>
/// <param name="FrameCount">The number of frames.</param>
internal readonly record struct AnimationFrameFormat(int Width, int Height, int FrameRate, int FrameCount);
