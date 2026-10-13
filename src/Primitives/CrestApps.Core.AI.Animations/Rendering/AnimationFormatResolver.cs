using System.Globalization;

namespace CrestApps.Core.AI.Animations.Rendering;

/// <summary>
/// Fills in what a request leaves out of a video's length, size and frame rate, and holds what it asks for within
/// the configured limits.
/// </summary>
/// <remarks>
/// A value outside a limit is brought inside it and reported as an adjustment, so the model can tell the user the
/// video is shorter or smaller than asked.
/// </remarks>
internal static class AnimationFormatResolver
{
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 180;
    private const int MinimumFrameRate = 12;
    private const double MinimumDurationSeconds = 1;

    /// <summary>
    /// Resolves a video's format.
    /// </summary>
    /// <param name="durationSeconds">The length asked for, or <see langword="null"/>.</param>
    /// <param name="width">The width asked for, or <see langword="null"/>.</param>
    /// <param name="height">The height asked for, or <see langword="null"/>.</param>
    /// <param name="frameRate">The frame rate asked for, or <see langword="null"/>.</param>
    /// <param name="options">The configured limits.</param>
    /// <param name="adjustments">Receives what was changed to fit the limits.</param>
    /// <returns>The frames, and the length in seconds.</returns>
    public static (AnimationFrameFormat Format, double DurationSeconds) Resolve(double? durationSeconds, int? width, int? height, int? frameRate, AnimationOptions options, List<string> adjustments)
    {
        var duration = Clamp(durationSeconds ?? options.DefaultDurationSeconds, MinimumDurationSeconds, options.MaxDurationSeconds, "duration_seconds", adjustments);
        var rate = Clamp(frameRate ?? options.DefaultFrameRate, MinimumFrameRate, options.MaxFrameRate, "frame_rate", adjustments);
        var format = new AnimationFrameFormat(
            Even(Clamp(width ?? options.DefaultWidth, MinimumWidth, options.MaxWidth, "width", adjustments)),
            Even(Clamp(height ?? options.DefaultHeight, MinimumHeight, options.MaxHeight, "height", adjustments)),
            rate,
            (int)Math.Round(duration * rate));

        return (format, duration);
    }

    private static int Even(int value)
    {
        // H.264 in yuv420p halves the colour planes, so both sides of the frame must be even.
        return value - (value % 2);
    }

    private static int Clamp(int value, int minimum, int maximum, string name, List<string> adjustments)
    {
        var clamped = Math.Clamp(value, minimum, maximum);

        if (clamped != value)
        {
            adjustments.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {value} is outside {minimum}–{maximum}; {clamped} was used."));
        }

        return clamped;
    }

    private static double Clamp(double value, double minimum, double maximum, string name, List<string> adjustments)
    {
        var clamped = Math.Clamp(value, minimum, maximum);

        if (clamped != value)
        {
            adjustments.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {value:0.##} is outside {minimum:0.##}–{maximum:0.##}; {clamped:0.##} was used."));
        }

        return clamped;
    }
}
