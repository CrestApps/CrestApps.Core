using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using CrestApps.Core.AI.Animations.Scripting;
using CrestApps.Core.AI.Animations.Video;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Services;

/// <summary>
/// Turns an animation script the model wrote into an MP4 and a poster still, inside the limits the host configured.
/// </summary>
/// <remarks>
/// A render keeps a CPU core busy for its whole length, so only <see cref="AnimationOptions.MaxConcurrentRenders"/>
/// run at once and the rest wait. Each works in a folder of its own, which is deleted when the render fails or
/// when the caller disposes the result; folders left behind by a crash are swept away by the next render.
/// </remarks>
internal sealed class AnimationRenderService : IDisposable
{
    private static readonly TimeSpan _staleWorkingFolderAge = TimeSpan.FromHours(1);

    private readonly AnimationOptions _options;
    private readonly FFmpegProbe _probe;
    private readonly FFmpegVideoEncoder _encoder;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnimationRenderService> _logger;
    private readonly SemaphoreSlim _renderSlots;
    private int _staleFoldersSwept;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationRenderService"/> class.
    /// </summary>
    /// <param name="options">The animation options.</param>
    /// <param name="probe">The FFmpeg probe.</param>
    /// <param name="encoder">The video encoder.</param>
    /// <param name="timeProvider">The time provider used to find working folders left behind by a crash.</param>
    /// <param name="logger">The logger.</param>
    public AnimationRenderService(
        IOptions<AnimationOptions> options,
        FFmpegProbe probe,
        FFmpegVideoEncoder encoder,
        TimeProvider timeProvider,
        ILogger<AnimationRenderService> logger)
    {
        _options = options.Value;
        _probe = probe;
        _encoder = encoder;
        _timeProvider = timeProvider;
        _logger = logger;
        _renderSlots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRenders));
    }

    /// <summary>
    /// Renders an animation script the model wrote.
    /// </summary>
    /// <param name="request">The script and the video it should make.</param>
    /// <param name="cancellationToken">The cancellation token of the turn that asked for it.</param>
    /// <returns>The rendered video; dispose it once the video has been stored.</returns>
    /// <exception cref="AnimationRenderException">The video could not be made within the limits.</exception>
    /// <exception cref="AnimationScriptException">The script failed or broke a limit.</exception>
    public async Task<AnimationRenderOutput> RenderScriptAsync(AnimationScriptRequest request, CancellationToken cancellationToken)
    {
        var format = request.Format;

        var probe = await _probe.GetResultAsync();

        if (!probe.IsAvailable)
        {
            throw new AnimationRenderException(probe.Reason);
        }

        await _renderSlots.WaitAsync(cancellationToken);

        var workingFolder = Path.Combine(_options.TempDirectory, Guid.NewGuid().ToString("N"));

        try
        {
            SweepStaleWorkingFolders();
            Directory.CreateDirectory(workingFolder);

            var videoPath = Path.Combine(workingFolder, "video.mp4");
            var posterFrame = Math.Min(format.FrameCount - 1, (int)(format.FrameCount * 0.4));
            byte[] poster = null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(_options.RenderTimeout);

            try
            {
                await _encoder.EncodeAsync(
                    probe.Encoder,
                    videoPath,
                    format.Width,
                    format.Height,
                    format.FrameRate,
                    async (input, token) => poster = await WriteFramesAsync(request, input, posterFrame, token),
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AnimationRenderException($"Rendering took longer than {_options.RenderTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds and was stopped. Draw a shorter, smaller or simpler scene.");
            }

            var video = new FileInfo(videoPath);

            if (!video.Exists || video.Length == 0)
            {
                throw new AnimationRenderException("FFmpeg finished without writing a video.");
            }

            if (video.Length > _options.MaxOutputBytes)
            {
                throw new AnimationRenderException($"The video came to {video.Length / (1024 * 1024)} MB, over the {_options.MaxOutputBytes / (1024 * 1024)} MB limit. Make the video shorter or smaller.");
            }

            return new AnimationRenderOutput(workingFolder, videoPath, poster, video.Length);
        }
        catch
        {
            DeleteFolder(workingFolder);

            throw;
        }
        finally
        {
            _renderSlots.Release();
        }
    }

    /// <summary>
    /// Runs a script's setup and a few of its frames without encoding anything, so a broken or slow script is sent
    /// back to the model in seconds rather than after a full render. The same run draws the stills a review judges,
    /// so the script is set up once for both. It takes a render slot, as a render does.
    /// </summary>
    /// <param name="request">The script and the video it should make.</param>
    /// <param name="stillTimes">The moments to draw for a review, in seconds; empty when there is no review.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The stills side by side in one PNG, each labelled with its time, or <see langword="null"/> when none were
    /// asked for.
    /// </returns>
    /// <exception cref="AnimationScriptException">The script failed, broke a limit, or would take too long to render.</exception>
    public async Task<byte[]> TestScriptAsync(AnimationScriptRequest request, IReadOnlyList<double> stillTimes, CancellationToken cancellationToken)
    {
        await _renderSlots.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(() => TestScript(request, stillTimes, cancellationToken), cancellationToken);
        }
        finally
        {
            _renderSlots.Release();
        }
    }

    /// <summary>
    /// Releases the render slots.
    /// </summary>
    public void Dispose()
    {
        _renderSlots.Dispose();
    }

    private byte[] TestScript(AnimationScriptRequest request, IReadOnlyList<double> stillTimes, CancellationToken cancellationToken)
    {
        var format = request.Format;
        var imageInfo = new SKImageInfo(format.Width, format.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(imageInfo) ?? throw new AnimationRenderException("The frame surface could not be created.");
        using var source = new ScriptFrameSource(request, _options, cancellationToken);

        // The first frame warms the engine up, so the two after it give a fair idea of the cost of a frame.
        var timedFrames = 2;
        var stopwatch = new Stopwatch();

        for (var frame = 0; frame <= timedFrames && frame < format.FrameCount; frame++)
        {
            if (frame == 1)
            {
                stopwatch.Start();
            }

            source.Render(surface.Canvas, (double)frame / format.FrameRate);
        }

        stopwatch.Stop();

        // An object designed one unit long and scaled by much less than its size in pixels draws nothing a viewer
        // can see, and neither the script nor a review can tell why. The arithmetic can, so it is reported here.
        var runner = source.Runner;

        if (runner.LargestSpeckCluster >= 8)
        {
            var spot = runner.SpeckClusterLocation;

            throw new AnimationScriptException($"{runner.LargestSpeckCluster} shapes collapsed into one spot a few pixels wide near ({spot.X:0}, {spot.Y:0}): a whole object is being drawn far too small to see. This happens when an object designed in small units (for example a body one unit long) is scaled by much less than its size in pixels. Scale it by its size in pixels — for example ctx.scale(480, 480) for a subject 480 pixels long, not size / 300 — and check every subject drawn this way.");
        }

        // The stills come after the timed frames and before the last one, so every frame is still drawn in time order.
        var earliestStill = (double)Math.Min(timedFrames, format.FrameCount - 1) / format.FrameRate;
        var stills = stillTimes.Count > 0
            ? DrawStills(source, surface, request, stillTimes, earliestStill)
            : null;

        if (format.FrameCount > timedFrames + 1)
        {
            source.Render(surface.Canvas, (double)(format.FrameCount - 1) / format.FrameRate);

            var estimate = TimeSpan.FromTicks(stopwatch.Elapsed.Ticks / timedFrames * format.FrameCount);

            if (estimate > _options.RenderTimeout * 0.9)
            {
                throw new AnimationScriptException($"Each frame takes about {stopwatch.Elapsed.TotalMilliseconds / timedFrames:0} ms, so {format.FrameCount} frames would take about {estimate.TotalSeconds:0} s, over the {_options.RenderTimeout.TotalSeconds:0} s limit. Draw fewer or simpler shapes per frame, or move repeated work into setup.");
            }
        }

        return stills;
    }

    private static byte[] DrawStills(ScriptFrameSource source, SKSurface frameSurface, AnimationScriptRequest request, IReadOnlyList<double> times, double earliest)
    {
        const int StillWidth = 640;

        var format = request.Format;
        var stillHeight = (int)Math.Round((double)StillWidth * format.Height / format.Width);
        var sheetInfo = new SKImageInfo(StillWidth * times.Count, stillHeight, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var sheetSurface = SKSurface.Create(sheetInfo) ?? throw new AnimationRenderException("The review surface could not be created.");
        using var labelFont = new SKFont(SKTypeface.Default, 22);
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var labelBackground = new SKPaint { Color = SKColors.Black.WithAlpha(170) };
        using var sampling = new SKPaint { IsAntialias = true };

        // Drawn in time order, as a video would be, so a script that keeps state between frames still works.
        var ordered = times.Select((time, position) => (Time: Math.Clamp(time, earliest, request.DurationSeconds), Position: position)).OrderBy(still => still.Time);

        foreach (var (time, position) in ordered)
        {
            source.Render(frameSurface.Canvas, time);

            using var frame = frameSurface.Snapshot();
            var target = SKRect.Create(position * StillWidth, 0, StillWidth, stillHeight);

            sheetSurface.Canvas.DrawImage(frame, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), sampling);
            sheetSurface.Canvas.DrawRect(target.Left + 8, 8, 110, 32, labelBackground);
            sheetSurface.Canvas.DrawText($"t = {time:0.0} s", target.Left + 16, 32, SKTextAlign.Left, labelFont, labelPaint);
        }

        using var sheet = sheetSurface.Snapshot();
        using var png = sheet.Encode(SKEncodedImageFormat.Png, 90);

        return png.ToArray();
    }

    private async Task<byte[]> WriteFramesAsync(AnimationScriptRequest request, Stream input, int posterFrame, CancellationToken cancellationToken)
    {
        var format = request.Format;

        // RGBA in that byte order, whatever the platform's native order is, because that is what FFmpeg is told
        // it receives; Skia's default on Windows is BGRA, which would swap red and blue.
        var imageInfo = new SKImageInfo(format.Width, format.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var frameBuffer = new byte[imageInfo.BytesSize];
        var pinnedFrame = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
        byte[] poster = null;

        try
        {
            // The surface draws straight into the buffer that is written to FFmpeg, so no frame is copied.
            using var surface = SKSurface.Create(imageInfo, pinnedFrame.AddrOfPinnedObject(), imageInfo.RowBytes)
                ?? throw new AnimationRenderException("The frame surface could not be created.");
            using var frameSource = new ScriptFrameSource(request, _options, cancellationToken);

            for (var frame = 0; frame < format.FrameCount; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                frameSource.Render(surface.Canvas, (double)frame / format.FrameRate);

                if (frame == posterFrame)
                {
                    using var image = surface.Snapshot();
                    using var png = image.Encode(SKEncodedImageFormat.Png, 100);

                    poster = png.ToArray();
                }

                await input.WriteAsync(frameBuffer, cancellationToken);
            }

            await input.FlushAsync(cancellationToken);
        }
        finally
        {
            pinnedFrame.Free();
        }

        return poster;
    }

    private void SweepStaleWorkingFolders()
    {
        if (Interlocked.Exchange(ref _staleFoldersSwept, 1) == 1 || !Directory.Exists(_options.TempDirectory))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var folder in Directory.EnumerateDirectories(_options.TempDirectory))
        {
            if (now - Directory.GetLastWriteTimeUtc(folder) > _staleWorkingFolderAge)
            {
                DeleteFolder(folder);
            }
        }
    }

    private void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the animation working folder '{Folder}'.", folder);
        }
    }
}
