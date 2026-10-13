using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Animations.Video;

/// <summary>
/// Finds out once whether FFmpeg can be run and which H.264 encoder it offers, and remembers the answer for the
/// life of the process.
/// </summary>
/// <remarks>
/// Asked on every turn by the agent provider, so the executable is started only the first time. A server that
/// gains FFmpeg later has to be restarted before the agent is offered.
/// </remarks>
internal sealed class FFmpegProbe
{
    private static readonly string[] _preferredEncoders = ["libx264", "libopenh264", "h264_mf"];
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(15);

    private readonly AnimationOptions _options;
    private readonly ILogger<FFmpegProbe> _logger;
    private readonly Lazy<Task<FFmpegProbeResult>> _result;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegProbe"/> class.
    /// </summary>
    /// <param name="options">The animation options.</param>
    /// <param name="logger">The logger.</param>
    public FFmpegProbe(
        IOptions<AnimationOptions> options,
        ILogger<FFmpegProbe> logger)
    {
        _options = options.Value;
        _logger = logger;
        _result = new Lazy<Task<FFmpegProbeResult>>(ProbeAsync);
    }

    /// <summary>
    /// Returns whether FFmpeg is usable and the encoder to use.
    /// </summary>
    public Task<FFmpegProbeResult> GetResultAsync()
    {
        return _result.Value;
    }

    private async Task<FFmpegProbeResult> ProbeAsync()
    {
        var startInfo = new ProcessStartInfo(_options.FFmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-encoders");

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The process did not start.");
            using var timeout = new CancellationTokenSource(_probeTimeout);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            string encoders;

            try
            {
                encoders = await process.StandardOutput.ReadToEndAsync(timeout.Token);

                await process.WaitForExitAsync(timeout.Token);
                await standardError;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);

                throw;
            }

            var encoder = SelectEncoder(encoders);

            if (encoder is null)
            {
                var reason = string.IsNullOrWhiteSpace(_options.VideoEncoder)
                    ? $"FFmpeg at '{_options.FFmpegPath}' offers none of the H.264 encoders {string.Join(", ", _preferredEncoders)}."
                    : $"FFmpeg at '{_options.FFmpegPath}' does not offer the configured encoder '{_options.VideoEncoder}'.";

                _logger.LogWarning("The animation agent is unavailable: {Reason}", reason);

                return FFmpegProbeResult.Unavailable(reason);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("FFmpeg at '{FFmpegPath}' will encode animations with '{Encoder}'.", _options.FFmpegPath, encoder);
            }

            return FFmpegProbeResult.Available(encoder);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException or IOException)
        {
            var reason = $"FFmpeg could not be run from '{_options.FFmpegPath}'. Install FFmpeg or set {AnimationOptions.ConfigurationSectionName}:FFmpegPath.";

            // Expected wherever FFmpeg is not installed, so the reason is logged without the stack trace.
            _logger.LogWarning("The animation agent is unavailable: {Reason} ({Error})", reason, ex.Message);

            return FFmpegProbeResult.Unavailable(reason);
        }
    }

    private string SelectEncoder(string encoderListing)
    {
        // Each listing line reads " V....D libx264  ...": flags, then the encoder's name.
        var offered = encoderListing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 1 && parts[0].StartsWith('V'))
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_options.VideoEncoder))
        {
            return offered.Contains(_options.VideoEncoder) ? _options.VideoEncoder : null;
        }

        return _preferredEncoders.FirstOrDefault(offered.Contains);
    }
}

/// <summary>
/// Whether FFmpeg is usable, and with which encoder.
/// </summary>
/// <param name="IsAvailable">Whether animations can be encoded.</param>
/// <param name="Encoder">The H.264 encoder to use, when available.</param>
/// <param name="Reason">Why animations cannot be encoded, when unavailable.</param>
internal sealed record FFmpegProbeResult(bool IsAvailable, string Encoder, string Reason)
{
    /// <summary>
    /// Creates the result for a usable FFmpeg.
    /// </summary>
    /// <param name="encoder">The H.264 encoder to use.</param>
    public static FFmpegProbeResult Available(string encoder) => new(true, encoder, null);

    /// <summary>
    /// Creates the result for an FFmpeg that cannot be used.
    /// </summary>
    /// <param name="reason">Why animations cannot be encoded.</param>
    public static FFmpegProbeResult Unavailable(string reason) => new(false, null, reason);
}
