using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Animations.Video;

/// <summary>
/// Turns raw frames into an H.264 MP4 by piping them to FFmpeg.
/// </summary>
/// <remarks>
/// FFmpeg is started without a shell, and every argument is either a validated number or a fixed value; nothing
/// the model wrote reaches its command line. Its error output is read while the frames are written, because a
/// full error pipe would stall FFmpeg and with it the frames. On cancellation the process and anything it
/// started are killed.
/// </remarks>
internal sealed class FFmpegVideoEncoder
{
    private const int MaxErrorOutputCharacters = 2_000;

    private readonly AnimationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegVideoEncoder"/> class.
    /// </summary>
    /// <param name="options">The animation options.</param>
    public FFmpegVideoEncoder(IOptions<AnimationOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Encodes a video.
    /// </summary>
    /// <param name="encoder">The H.264 encoder FFmpeg is asked to use.</param>
    /// <param name="outputPath">The MP4 file to write.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="frameRate">The frames per second.</param>
    /// <param name="writeFramesAsync">Writes every frame, as tightly packed RGBA rows, to the stream it is given.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task EncodeAsync(
        string encoder,
        string outputPath,
        int width,
        int height,
        int frameRate,
        Func<Stream, CancellationToken, Task> writeFramesAsync,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateStartInfo(encoder, outputPath, width, height, frameRate))
            ?? throw new AnimationRenderException($"FFmpeg could not be started from '{_options.FFmpegPath}'.");

        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

        try
        {
            try
            {
                await writeFramesAsync(process.StandardInput.BaseStream, cancellationToken);
            }
            catch (IOException)
            {
                // FFmpeg closed its input early, which means it failed; its exit code and error output below say
                // why, which the broken pipe does not.
            }
            finally
            {
                CloseInput(process);
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            // Cancelled, timed out, or the frames failed to draw: FFmpeg must not be left running on a half-written
            // file the caller is about to delete.
            Kill(process);

            throw;
        }

        await standardOutput;
        var errorOutput = await standardError;

        if (process.ExitCode != 0)
        {
            throw new AnimationRenderException($"FFmpeg failed with exit code {process.ExitCode}: {Truncate(errorOutput)}");
        }
    }

    private ProcessStartInfo CreateStartInfo(string encoder, string outputPath, int width, int height, int frameRate)
    {
        var startInfo = new ProcessStartInfo(_options.FFmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var arguments = startInfo.ArgumentList;

        arguments.Add("-hide_banner");
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-y");
        arguments.Add("-f");
        arguments.Add("rawvideo");
        arguments.Add("-pix_fmt");
        arguments.Add("rgba");
        arguments.Add("-s");
        arguments.Add(string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"));
        arguments.Add("-r");
        arguments.Add(frameRate.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-i");
        arguments.Add("pipe:0");
        arguments.Add("-an");
        arguments.Add("-c:v");
        arguments.Add(encoder);

        if (string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-preset");
            arguments.Add("veryfast");
            arguments.Add("-crf");
            arguments.Add(_options.ConstantRateFactor.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add("-b:v");
            arguments.Add(string.Create(CultureInfo.InvariantCulture, $"{_options.BitRateKilobits}k"));
        }

        // yuv420p is the only pixel format every browser plays, and faststart puts the index at the front of
        // the file so the player can start before the whole video has downloaded.
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add(outputPath);

        return startInfo;
    }

    private static void CloseInput(Process process)
    {
        try
        {
            // Closing the input is how FFmpeg learns the last frame has been sent.
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The pipe is already broken because FFmpeg exited; its exit code says why.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill.
        }
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "no error output";
        }

        var trimmed = text.Trim();

        return trimmed.Length <= MaxErrorOutputCharacters ? trimmed : trimmed[..MaxErrorOutputCharacters] + "…";
    }
}
