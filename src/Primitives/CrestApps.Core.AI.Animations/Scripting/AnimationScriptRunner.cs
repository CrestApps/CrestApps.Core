using System.Globalization;
using Jint;
using Jint.Runtime;
using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// Runs an animation script the model wrote, one frame at a time, inside a Jint JavaScript engine.
/// </summary>
/// <remarks>
/// The engine is the sandbox. It has no file, network, process or .NET access: interop is off, nothing from the
/// host is handed to the script, and the only things that cross between them are numbers and strings. Every call
/// into the script — setup and each frame — runs under its own time, statement, memory and recursion limits, so a
/// runaway loop or a memory bomb fails that frame with a message instead of tying up the server.
/// </remarks>
internal sealed class AnimationScriptRunner : IDisposable
{
    private const string ScriptSourceName = "animation.js";

    private static readonly Lazy<string> _prelude = new(LoadPrelude);

    private readonly Engine _engine;
    private readonly CanvasCommandPlayer _player = new();
    private readonly int _maxCommandsPerFrame;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationScriptRunner"/> class, runs the script and its
    /// <c>setup</c>.
    /// </summary>
    /// <param name="request">The script and the video it should make.</param>
    /// <param name="options">The animation options, for the limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="AnimationScriptException">The script failed or broke a limit.</exception>
    public AnimationScriptRunner(
        AnimationScriptRequest request,
        AnimationOptions options,
        CancellationToken cancellationToken)
    {
        _maxCommandsPerFrame = options.MaxScriptCommandsPerFrame;
        _engine = new Engine(engineOptions =>
        {
            engineOptions
                .TimeoutInterval(options.ScriptFrameTimeout)
                .MaxStatements(options.MaxScriptStatementsPerFrame)
                .LimitMemory(options.MaxScriptMemoryBytes)
                .LimitRecursion(256)
                .CancellationToken(cancellationToken);

            engineOptions.Constraints.MaxArraySize = (uint)Math.Max(options.MaxScriptCommandsPerFrame, 1_000_000);
        });

        var format = request.Format;
        var info = string.Create(
            CultureInfo.InvariantCulture,
            $"const __info = Object.freeze({{ width: {format.Width}, height: {format.Height}, frameRate: {format.FrameRate}, duration: {request.DurationSeconds}, frameCount: {format.FrameCount}, seed: {request.Seed} }});");

        Run(() =>
        {
            _engine.Execute(info, "info.js");
            _engine.Execute(_prelude.Value, "prelude.js");
            _engine.Execute(request.Script, ScriptSourceName);
            _engine.Invoke("__setup");
        });
    }

    /// <summary>
    /// Draws the frame at a moment. Frames must be drawn in order: the script may keep state between them.
    /// </summary>
    /// <param name="canvas">The frame canvas, already cleared.</param>
    /// <param name="time">The time in seconds.</param>
    /// <exception cref="AnimationScriptException">The script failed or broke a limit.</exception>
    public void RenderFrame(SKCanvas canvas, double time)
    {
        Run(() =>
        {
            var commands = _engine.Invoke("__frame", time).AsArray();

            if (commands.Length > _maxCommandsPerFrame)
            {
                throw new AnimationScriptException($"The frame at {time.ToString("0.00", CultureInfo.InvariantCulture)} s recorded more than {_maxCommandsPerFrame} drawing values. Draw fewer shapes per frame, or work out repeated geometry once in setup.");
            }

            _player.Play(canvas, commands, _engine.GetValue("__strings").AsArray());
        });
    }

    /// <summary>
    /// Gets the most shapes that collapsed into one 8-pixel spot in a single frame.
    /// </summary>
    public int LargestSpeckCluster => _player.LargestSpeckCluster;

    /// <summary>
    /// Gets where the largest cluster of specks was.
    /// </summary>
    public SKPoint SpeckClusterLocation => _player.SpeckClusterLocation;

    /// <summary>
    /// Releases the drawing state and the script engine.
    /// </summary>
    public void Dispose()
    {
        _player.Dispose();
        _engine.Dispose();
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExecutionCanceledException)
        {
            throw new OperationCanceledException();
        }
        catch (AnimationScriptException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AnimationScriptException(Describe(ex), ex);
        }
    }

    private static string Describe(Exception exception)
    {
        return exception switch
        {
            // A syntax error already names its place ("animation.js:2:25"); a runtime error does not.
            JavaScriptException javaScript when string.Equals(javaScript.Location.SourceFile, ScriptSourceName, StringComparison.Ordinal) && !javaScript.Message.Contains(ScriptSourceName, StringComparison.Ordinal)
                => $"{javaScript.Message} (line {javaScript.Location.Start.Line}, column {javaScript.Location.Start.Column + 1}).",
            JavaScriptException javaScript => javaScript.Message,
            TimeoutException => "One call took longer than the time allowed for a single frame. Do the heavy work once in setup and keep each frame light.",
            StatementsCountOverflowException => "One call ran more statements than a single frame allows. Avoid per-pixel loops; draw shapes instead.",
            MemoryLimitExceededException => "The script used more memory than allowed. Keep arrays and per-frame allocations small.",
            RecursionDepthOverflowException => "The script recursed too deeply.",
            _ => exception.Message,
        };
    }

    private static string LoadPrelude()
    {
        using var stream = typeof(AnimationScriptRunner).Assembly.GetManifestResourceStream("CrestApps.Core.AI.Animations.Scripting.canvas-prelude.js")
            ?? throw new InvalidOperationException("The canvas prelude is missing from the assembly.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
