namespace CrestApps.Core.AI.Realtime.WebRtc;

/// <summary>
/// Halves the sample rate of a PCM16 mono stream (48 kHz to 24 kHz) with a low-pass FIR so content above the new
/// Nyquist frequency does not alias into the speech band. Stateful: carries the filter history across calls so
/// packet boundaries are seamless.
/// </summary>
internal sealed class Pcm48To24Decimator
{
    // Windowed-sinc low-pass (Hamming), cutoff at 10.5 kHz on a 48 kHz stream: comfortably below the 12 kHz
    // Nyquist of the 24 kHz output, with a short enough transition band for 31 taps to reject aliases well.
    private const int Taps = 31;
    private const double Cutoff = 10500.0 / 48000.0;

    private static readonly float[] _coefficients = BuildCoefficients();

    // The last (Taps - 1) input samples of the previous call.
    private readonly float[] _history = new float[Taps - 1];

    /// <summary>
    /// Decimates <paramref name="input"/> (48 kHz) into <paramref name="output"/> (24 kHz) and returns the number
    /// of output samples written (half the input length, rounded down).
    /// </summary>
    public int Process(ReadOnlySpan<short> input, Span<short> output)
    {
        var outputCount = input.Length / 2;

        if (outputCount > output.Length)
        {
            throw new ArgumentException("The output buffer is too small for the decimated frame.", nameof(output));
        }

        // Work on a contiguous buffer: history followed by the new samples, so each output tap window is a plain
        // slice. Sized for the largest Opus frame (120 ms) plus history; allocated per call would churn, so reuse.
        var work = _work.Length >= _history.Length + input.Length ? _work : (_work = new float[_history.Length + input.Length]);
        _history.CopyTo(work, 0);
        for (var i = 0; i < input.Length; i++)
        {
            work[_history.Length + i] = input[i];
        }

        for (var o = 0; o < outputCount; o++)
        {
            // Output sample o corresponds to input sample 2o; the FIR is centred on it, reaching back Taps-1 samples
            // into the buffer (which is why the history is prepended).
            var start = 2 * o;
            var acc = 0f;
            for (var t = 0; t < Taps; t++)
            {
                acc += work[start + t] * _coefficients[t];
            }

            output[o] = (short)Math.Clamp((int)MathF.Round(acc), short.MinValue, short.MaxValue);
        }

        // Keep the tail of this call's input as the next call's history.
        var total = _history.Length + input.Length;
        Array.Copy(work, total - _history.Length, _history, 0, _history.Length);

        return outputCount;
    }

    private float[] _work = [];

    private static float[] BuildCoefficients()
    {
        var taps = new float[Taps];
        var middle = (Taps - 1) / 2.0;
        var sum = 0.0;

        for (var i = 0; i < Taps; i++)
        {
            var n = i - middle;
            var sinc = n == 0 ? 2 * Cutoff : Math.Sin(2 * Math.PI * Cutoff * n) / (Math.PI * n);
            var window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (Taps - 1));
            taps[i] = (float)(sinc * window);
            sum += taps[i];
        }

        // Normalise to unity gain at DC so levels are preserved exactly.
        for (var i = 0; i < Taps; i++)
        {
            taps[i] = (float)(taps[i] / sum);
        }

        return taps;
    }
}
