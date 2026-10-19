using CrestApps.Core.AI.OpenAI.Services;

namespace CrestApps.Core.Tests.Framework.AI.OpenAI;

/// <summary>
/// OpenAI speaks raw PCM at 24 kHz only, so a cascaded realtime session at another rate (8 kHz or 16 kHz for a phone
/// line) has the audio converted as it streams (CrestApps.Core#279).
/// </summary>
public sealed class PcmResamplerTests
{
    private const int InputRate = 24000;

    [Theory]
    [InlineData(8000)]
    [InlineData(16000)]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Resample_GivesOneSampleForEveryPointInTimeTheInputCovered(int outputRate)
    {
        // Arrange
        var input = Tone(1000, InputRate, InputRate / 2, 0.5);

        // Act
        var output = ResampleWhole(input, outputRate);

        // Assert
        Assert.Equal((int)Math.Ceiling(input.Length * (double)outputRate / InputRate), output.Length);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Resample_KeepsAToneAtItsPitchAndLevel(int outputRate)
    {
        // Arrange
        // Played at the wrong rate, a 1 kHz tone would come out at another pitch, which is what the refusal guarded
        // against.
        const int Frequency = 1000;
        var input = Tone(Frequency, InputRate, InputRate, 0.5);

        // Act
        var output = ResampleWhole(input, outputRate);

        // Assert
        var steady = output[(outputRate / 10)..^(outputRate / 10)];
        var crossings = ZeroCrossings(steady);
        var seconds = steady.Length / (double)outputRate;
        Assert.InRange(crossings / seconds / 2, Frequency * 0.99, Frequency * 1.01);
        Assert.InRange(Rms(steady) / Rms(input), 0.97, 1.03);
    }

    [Fact]
    public void Resample_DownToAPhoneLine_RemovesWhatThePhoneLineCannotCarry()
    {
        // Arrange
        // A 10 kHz tone has no place at 8 kHz. Dropping samples without filtering would fold it back to 2 kHz, as
        // noise the caller hears.
        var input = Tone(10000, InputRate, InputRate, 0.5);

        // Act
        var output = ResampleWhole(input, 8000);

        // Assert
        var steady = output[800..^800];
        Assert.True(Rms(steady) < Rms(input) / 100, $"The tone came through at {Rms(steady):F1} RMS against {Rms(input):F1}.");
    }

    [Fact]
    public void Resample_KeepsASteadyLevel()
    {
        // Arrange
        var input = Enumerable.Repeat((short)1000, InputRate / 10).ToArray();

        // Act
        var output = ResampleWhole(input, 16000);

        // Assert
        Assert.All(output[100..^100], sample => Assert.InRange(sample, 999, 1001));
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(16000)]
    [InlineData(44100)]
    public void Resample_InChunks_GivesTheSameAudioAsInOnePiece_WhereverTheStreamIsSplit(int outputRate)
    {
        // Arrange
        // A stream arrives in chunks of any size, some ending half-way through a sample.
        var input = Tone(440, InputRate, InputRate / 4, 0.8);
        var bytes = ToBytes(input);
        var whole = ResampleWhole(input, outputRate);
        var random = new Random(279);
        var resampler = new PcmResampler(InputRate, outputRate);
        List<byte> chunked = [];

        // Act
        for (var offset = 0; offset < bytes.Length;)
        {
            var length = Math.Min(random.Next(1, 700), bytes.Length - offset);
            chunked.AddRange(resampler.Process(bytes.AsSpan(offset, length)));
            offset += length;
        }

        chunked.AddRange(resampler.Flush());

        // Assert
        Assert.Equal(whole, ToSamples([.. chunked]));
    }

    [Fact]
    public void Resample_LoudAudio_IsClippedRatherThanWrappedAround()
    {
        // Arrange
        // A full-scale square wave rings past full scale when filtered. Wrapping around would turn the peak into a
        // full-scale crack of the opposite sign.
        var input = Enumerable.Range(0, InputRate / 10).Select(i => (i / 24) % 2 == 0 ? short.MaxValue : short.MinValue).ToArray();

        // Act
        var output = ResampleWhole(input, 16000);

        // Assert
        // Each half of the wave is 24 input samples, so 16 output samples. Away from the edges, every sample keeps the
        // half's sign.
        for (var i = 16; i < output.Length - 16; i++)
        {
            if (i % 16 is < 2 or > 13)
            {
                continue;
            }

            var positive = i / 16 % 2 == 0;
            Assert.True(positive ? output[i] > 0 : output[i] < 0, $"Sample {i} is {output[i]}.");
        }

        Assert.Contains(short.MaxValue, output);
    }

    private static short[] ResampleWhole(short[] input, int outputRate)
    {
        var resampler = new PcmResampler(InputRate, outputRate);

        return ToSamples([.. resampler.Process(ToBytes(input)), .. resampler.Flush()]);
    }

    private static short[] Tone(double frequency, int rate, int count, double amplitude)
        => [.. Enumerable.Range(0, count).Select(i => (short)Math.Round(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * frequency * i / rate)))];

    private static byte[] ToBytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        return bytes;
    }

    private static short[] ToSamples(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);

        return samples;
    }

    private static double Rms(short[] samples)
        => Math.Sqrt(samples.Average(sample => (double)sample * sample));

    private static int ZeroCrossings(short[] samples)
    {
        var crossings = 0;

        for (var i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0) != (samples[i] < 0))
            {
                crossings++;
            }
        }

        return crossings;
    }
}
