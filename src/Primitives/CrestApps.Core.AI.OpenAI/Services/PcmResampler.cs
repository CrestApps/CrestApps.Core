namespace CrestApps.Core.AI.OpenAI.Services;

/// <summary>
/// Converts a stream of 16-bit, little-endian, mono PCM from one sample rate to another, a chunk at a time.
/// </summary>
/// <remarks>
/// A windowed-sinc filter evaluated at the exact rational ratio of the two rates. The filter's history, the position
/// between input samples and a byte left over from an odd-length chunk are all carried from one chunk to the next, so
/// where a stream happens to be split makes no difference to the audio: no click at a chunk boundary, and no drift.
/// Going down in rate, the filter also removes what the lower rate cannot carry, which would otherwise fold back as
/// noise (24 kHz to 8 kHz for a phone line, for example).
/// </remarks>
internal sealed class PcmResampler
{
    // Zero crossings of the sinc on each side of the point being computed, at the filter's cutoff. More is sharper
    // and slower; 16 keeps the passband flat to a few tenths of a decibel.
    private const int ZeroCrossings = 16;

    // The cutoff, as a fraction of the lower rate's Nyquist frequency, leaving room for the filter to roll off
    // before it.
    private const double Cutoff = 0.92;

    private readonly int _up;
    private readonly int _down;
    private readonly int _halfWidth;
    private readonly int _taps;
    private readonly float[][] _phases;

    // Input samples still needed, the first of which is input sample _bufferStart. Samples before the stream began
    // are silence.
    private float[] _buffer;
    private int _buffered;
    private long _bufferStart;

    private long _received;
    private long _produced;
    private byte? _oddByte;

    /// <summary>
    /// Initializes a new instance of the <see cref="PcmResampler"/> class.
    /// </summary>
    /// <param name="inputRate">The rate of the samples given.</param>
    /// <param name="outputRate">The rate of the samples wanted.</param>
    public PcmResampler(int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);

        var divisor = GreatestCommonDivisor(inputRate, outputRate);
        _up = outputRate / divisor;
        _down = inputRate / divisor;

        var cutoff = Math.Min(1.0, (double)outputRate / inputRate) * Cutoff;
        var reach = ZeroCrossings / cutoff;
        _halfWidth = (int)Math.Ceiling(reach);
        _taps = 2 * _halfWidth;

        // One filter for each place an output sample can fall between two input samples. Output sample n falls at
        // input position n * down / up: on input sample k, (n * down) % up up-ths of the way to the next.
        _phases = new float[_up][];

        for (var phase = 0; phase < _up; phase++)
        {
            var coefficients = new double[_taps];
            var sum = 0.0;

            for (var tap = 0; tap < _taps; tap++)
            {
                // Tap 0 is input sample k - halfWidth + 1, the last tap is k + halfWidth.
                var distance = (double)phase / _up + _halfWidth - 1 - tap;
                var coefficient = cutoff * Sinc(cutoff * distance) * Blackman(distance / reach);
                coefficients[tap] = coefficient;
                sum += coefficient;
            }

            // Unity gain at DC for every phase, so a steady level comes out at the level it went in.
            _phases[phase] = [.. coefficients.Select(coefficient => (float)(coefficient / sum))];
        }

        _buffer = new float[Math.Max(_taps * 4, 4096)];
        _buffered = _halfWidth;
        _bufferStart = -_halfWidth;
    }

    /// <summary>
    /// Converts the next chunk of the stream and returns the samples that are ready, as 16-bit little-endian PCM.
    /// </summary>
    /// <remarks>
    /// The last few output samples of a chunk wait for the input that follows it, so call <see cref="Flush"/> once the
    /// stream ends.
    /// </remarks>
    /// <param name="pcm">The next bytes of the stream. A chunk may end part-way through a sample.</param>
    public byte[] Process(ReadOnlySpan<byte> pcm)
    {
        Append(pcm);

        return Produce(_received * _up);
    }

    /// <summary>
    /// Returns the samples still held back once the stream has ended. Nothing more can be given after this.
    /// </summary>
    public byte[] Flush()
    {
        // What is past the end is silence, as what was before the start was. An odd byte left over is half a sample,
        // and is dropped.
        EnsureCapacity(_halfWidth);
        Array.Clear(_buffer, _buffered, _halfWidth);
        _buffered += _halfWidth;
        _oddByte = null;

        // An output sample is owed for every point in time the input covered.
        return Produce(_received * _up);
    }

    private void Append(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
        {
            return;
        }

        var bytes = pcm;
        Span<byte> joined = stackalloc byte[2];

        if (_oddByte is byte low)
        {
            joined[0] = low;
            joined[1] = bytes[0];
            bytes = bytes[1..];
            _oddByte = null;
            AppendSamples(joined);
        }

        if (bytes.Length % 2 == 1)
        {
            _oddByte = bytes[^1];
            bytes = bytes[..^1];
        }

        AppendSamples(bytes);
    }

    private void AppendSamples(ReadOnlySpan<byte> bytes)
    {
        var count = bytes.Length / 2;

        if (count == 0)
        {
            return;
        }

        EnsureCapacity(count);

        for (var i = 0; i < count; i++)
        {
            _buffer[_buffered + i] = (short)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8));
        }

        _buffered += count;
        _received += count;
    }

    private byte[] Produce(long limit)
    {
        // Output sample n lies at input position n * down / up, and needs the input up to halfWidth samples past it.
        var available = _bufferStart + _buffered;
        var output = new List<byte>();

        while (true)
        {
            var position = _produced * _down;

            // Only the points in time the input has covered so far.
            if (position >= limit)
            {
                break;
            }

            var sample = position / _up;

            if (sample + _halfWidth >= available)
            {
                break;
            }

            var phase = (int)(position % _up);
            var coefficients = _phases[phase];
            var first = (int)(sample - _halfWidth + 1 - _bufferStart);
            var value = 0f;

            for (var tap = 0; tap < _taps; tap++)
            {
                value += coefficients[tap] * _buffer[first + tap];
            }

            var rounded = (short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue);
            output.Add((byte)rounded);
            output.Add((byte)(rounded >> 8));
            _produced++;
        }

        Discard();

        return [.. output];
    }

    // Drops the input no output sample still to come will read.
    private void Discard()
    {
        var oldestNeeded = (_produced * _down / _up) - _halfWidth + 1;
        var drop = (int)Math.Clamp(oldestNeeded - _bufferStart, 0, _buffered);

        if (drop == 0)
        {
            return;
        }

        Array.Copy(_buffer, drop, _buffer, 0, _buffered - drop);
        _buffered -= drop;
        _bufferStart += drop;
    }

    private void EnsureCapacity(int more)
    {
        if (_buffered + more <= _buffer.Length)
        {
            return;
        }

        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _buffered + more));
    }

    private static double Sinc(double x)
    {
        if (x == 0)
        {
            return 1;
        }

        var radians = Math.PI * x;

        return Math.Sin(radians) / radians;
    }

    private static double Blackman(double x)
    {
        if (Math.Abs(x) >= 1)
        {
            return 0;
        }

        return 0.42 + (0.5 * Math.Cos(Math.PI * x)) + (0.08 * Math.Cos(2 * Math.PI * x));
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
