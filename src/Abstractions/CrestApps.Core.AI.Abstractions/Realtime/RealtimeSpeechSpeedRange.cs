namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// The speaking speeds a realtime conversation offers, as a multiple of the model's normal pace.
/// </summary>
/// <remarks>
/// The provider stretches the generated audio, so the words and the voice are unchanged. It rejects anything above 1.5.
/// </remarks>
public static class RealtimeSpeechSpeedRange
{
    /// <summary>
    /// The slowest speed offered.
    /// </summary>
    public const double Minimum = 0.75;

    /// <summary>
    /// The fastest speed offered, and the fastest the provider accepts.
    /// </summary>
    public const double Maximum = 1.5;

    /// <summary>
    /// The model's normal pace, where every conversation starts.
    /// </summary>
    public const double Default = 1.0;

    /// <summary>
    /// Clamps a requested speed into the range, rounded to hundredths.
    /// </summary>
    /// <param name="speed">The requested speed.</param>
    /// <returns>The speed to use, or <see cref="Default"/> when <paramref name="speed"/> is not a number.</returns>
    public static double Normalize(double speed)
        => double.IsFinite(speed) ? Math.Round(Math.Clamp(speed, Minimum, Maximum), 2) : Default;
}
