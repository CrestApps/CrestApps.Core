namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Turns a profile's interruption sensitivity into the thresholds the cascaded and native realtime pipelines use.
/// </summary>
public static class TurnTaking
{
    /// <summary>
    /// Gets the number of words of caller speech needed before the AI is interrupted in a cascaded session.
    /// <see langword="null"/> (the default) and high sensitivities interrupt on the first word, which is how the
    /// pipeline behaved before the setting existed; <c>0</c> never interrupts.
    /// </summary>
    /// <param name="sensitivity">The profile's interruption sensitivity, from 0 to 1.</param>
    /// <returns>The word count, or <see cref="int.MaxValue"/> when the AI must never be interrupted.</returns>
    public static int MinimumWordsToInterrupt(double? sensitivity)
    {
        if (sensitivity is not { } value || !double.IsFinite(value))
        {
            return 1;
        }

        return value switch
        {
            <= 0 => int.MaxValue,
            < 0.34 => 5,
            < 0.67 => 3,
            _ => 1,
        };
    }

    /// <summary>
    /// Maps a sensitivity to a server voice-activity threshold for a native realtime session: a more sensitive
    /// profile needs quieter speech to interrupt. Returns <see langword="null"/> when none is set.
    /// </summary>
    /// <param name="sensitivity">The profile's interruption sensitivity, from 0 to 1.</param>
    public static float? ToVadThreshold(double? sensitivity)
    {
        if (sensitivity is not { } value || !double.IsFinite(value))
        {
            return null;
        }

        // 1 -> 0.2 (very easily triggered), 0 -> 0.9 (needs loud, deliberate speech).
        return (float)(0.9 - (Math.Clamp(value, 0, 1) * 0.7));
    }

    /// <summary>
    /// Counts the words in a transcript.
    /// </summary>
    /// <param name="text">The transcript.</param>
    public static int CountWords(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
