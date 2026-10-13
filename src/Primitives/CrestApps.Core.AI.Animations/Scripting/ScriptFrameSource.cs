using CrestApps.Core.AI.Animations.Rendering;
using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// Draws the frames of a video from an animation script, then lays the requested film look over each one.
/// </summary>
internal sealed class ScriptFrameSource : IDisposable
{
    private readonly AnimationScriptRunner _runner;
    private readonly CinematicEffects _effects;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptFrameSource"/> class and runs the script's setup.
    /// </summary>
    /// <param name="request">The script and the video it should make.</param>
    /// <param name="options">The animation options, for the limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ScriptFrameSource(
        AnimationScriptRequest request,
        AnimationOptions options,
        CancellationToken cancellationToken)
    {
        _runner = new AnimationScriptRunner(request, options, cancellationToken);

        if (!request.Look.IsNone)
        {
            _effects = new CinematicEffects(request.Look, request.Format.Width, request.Format.Height);
        }
    }

    /// <summary>
    /// Gets the script runner, for what it has drawn so far.
    /// </summary>
    public AnimationScriptRunner Runner => _runner;

    /// <summary>
    /// Draws the frame at the given time.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="time">The time of the frame, in seconds.</param>
    public void Render(SKCanvas canvas, double time)
    {
        // A script that leaves part of the frame unpainted gets black there, as a video has nothing behind it.
        canvas.Clear(SKColors.Black);
        _runner.RenderFrame(canvas, time);
        _effects?.Apply(canvas);
    }

    /// <summary>
    /// Releases the script engine and the film look.
    /// </summary>
    public void Dispose()
    {
        _runner.Dispose();
        _effects?.Dispose();
    }
}
