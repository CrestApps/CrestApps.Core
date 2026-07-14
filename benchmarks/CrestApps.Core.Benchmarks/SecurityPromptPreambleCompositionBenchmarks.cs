using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Defines the construction shape of the existing system-message builder.
/// </summary>
public enum SecurityPromptBuilderShape
{
    /// <summary>
    /// The existing message occupies one pre-sized builder chunk.
    /// </summary>
    Contiguous,

    /// <summary>
    /// The existing message is accumulated through repeated small appends.
    /// </summary>
    ManyAppends,
}

/// <summary>
/// Compares the captured security-preamble composition with the retained in-place insertion.
/// Input construction and exact-output verification occur outside measurement. This class must
/// remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[InvocationCount(1)]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SecurityPromptPreambleCompositionBenchmarks
{
    private const int AppendChunkSize = 64;
    private const int OperationsPerInvoke = 16;
    private static readonly string _systemMessageSeparator = string.Concat(Environment.NewLine, Environment.NewLine);

    private StringBuilder[] _builders;
    private string _existing;
    private string _preamble;

    /// <summary>
    /// Gets or sets the ASCII size of the existing system message.
    /// </summary>
    [Params(0, 256, 8 * 1024, 1024 * 1024)]
    public int ExistingMessageBytes { get; set; }

    /// <summary>
    /// Gets or sets the existing builder construction shape.
    /// </summary>
    [Params(SecurityPromptBuilderShape.Contiguous, SecurityPromptBuilderShape.ManyAppends)]
    public SecurityPromptBuilderShape BuilderShape { get; set; }

    /// <summary>
    /// Gets or sets the ASCII size of the rendered security preamble.
    /// </summary>
    [Params(256, 4 * 1024)]
    public int PreambleBytes { get; set; }

    /// <summary>
    /// Creates immutable inputs and proves every candidate produces the exact legacy output.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _existing = CreatePayload(ExistingMessageBytes, 'e');
        _preamble = CreatePayload(PreambleBytes, 'p');

        var shapeProbe = CreateBuilder();
        var chunkCount = CountChunks(shapeProbe);

        if (ExistingMessageBytes > 0 &&
            BuilderShape == SecurityPromptBuilderShape.Contiguous &&
            chunkCount != 1)
        {
            throw new InvalidOperationException($"Expected one contiguous chunk but observed {chunkCount}.");
        }

        if (ExistingMessageBytes > 0 &&
            BuilderShape == SecurityPromptBuilderShape.ManyAppends &&
            chunkCount <= 1)
        {
            throw new InvalidOperationException("Expected repeated appends to create multiple chunks.");
        }

        var legacyBuilder = CreateBuilder();
        var currentBuilder = CreateBuilder();
        var expected = ExistingMessageBytes == 0
            ? _preamble
            : string.Concat(_preamble, _systemMessageSeparator, _existing);

        EnsureEquivalent(expected, legacyBuilder, ComposeLegacy(legacyBuilder, _preamble));
        EnsureEquivalent(expected, currentBuilder, ComposeCurrent(currentBuilder, _preamble));
    }

    /// <summary>
    /// Recreates the requested builder state before every measured invocation.
    /// </summary>
    [IterationSetup]
    public void ResetBuilders()
    {
        _builders = new StringBuilder[OperationsPerInvoke];

        for (var index = 0; index < _builders.Length; index++)
        {
            _builders[index] = CreateBuilder();
        }
    }

    /// <summary>
    /// Composes by materializing, clearing, and appending the existing system message.
    /// </summary>
    /// <returns>A checksum of the composed builder lengths.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvoke)]
    [BenchmarkCategory("Composition")]
    public int LegacyCopyClearAppend()
    {
        var checksum = 0;

        foreach (var builder in _builders)
        {
            checksum += ComposeLegacy(builder, _preamble).Length;
        }

        return checksum;
    }

    /// <summary>
    /// Composes by inserting the cached separator and then the preamble at index zero.
    /// </summary>
    /// <returns>A checksum of the composed builder lengths.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    [BenchmarkCategory("Composition")]
    public int CurrentInPlaceInsert()
    {
        var checksum = 0;

        foreach (var builder in _builders)
        {
            checksum += ComposeCurrent(builder, _preamble).Length;
        }

        return checksum;
    }

    /// <summary>
    /// Composes with the captured implementation and flushes the final builder to a string.
    /// </summary>
    /// <returns>A checksum of the flushed system messages.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvoke)]
    [BenchmarkCategory("CompositionAndFlush")]
    public int LegacyCopyClearAppendAndFlush()
    {
        var checksum = 0;

        foreach (var builder in _builders)
        {
            checksum = AddToChecksum(checksum, ComposeLegacy(builder, _preamble).ToString());
        }

        return checksum;
    }

    /// <summary>
    /// Composes with the retained in-place insertion and flushes the final builder to a string.
    /// </summary>
    /// <returns>A checksum of the flushed system messages.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    [BenchmarkCategory("CompositionAndFlush")]
    public int CurrentInPlaceInsertAndFlush()
    {
        var checksum = 0;

        foreach (var builder in _builders)
        {
            checksum = AddToChecksum(checksum, ComposeCurrent(builder, _preamble).ToString());
        }

        return checksum;
    }

    /// <summary>
    /// Creates the requested existing-message builder shape.
    /// </summary>
    /// <returns>The populated builder.</returns>
    private StringBuilder CreateBuilder()
    {
        if (BuilderShape == SecurityPromptBuilderShape.Contiguous)
        {
            var builder = new StringBuilder(Math.Max(16, _existing.Length));
            builder.Append(_existing);

            return builder;
        }

        var chunkedBuilder = new StringBuilder(1);

        for (var offset = 0; offset < _existing.Length; offset += AppendChunkSize)
        {
            chunkedBuilder.Append(
                _existing,
                offset,
                Math.Min(AppendChunkSize, _existing.Length - offset));
        }

        return chunkedBuilder;
    }

    /// <summary>
    /// Reproduces the captured production implementation.
    /// </summary>
    /// <param name="builder">The existing system-message builder.</param>
    /// <param name="preamble">The rendered security preamble.</param>
    /// <returns>The mutated builder.</returns>
    private static StringBuilder ComposeLegacy(StringBuilder builder, string preamble)
    {
        var existingSystemMessage = builder.ToString();
        builder.Clear();
        builder.Append(preamble);

        if (!string.IsNullOrEmpty(existingSystemMessage))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(existingSystemMessage);
        }

        return builder;
    }

    /// <summary>
    /// Prepends the separator first so both insertions occur at index zero.
    /// </summary>
    /// <param name="builder">The existing system-message builder.</param>
    /// <param name="preamble">The rendered security preamble.</param>
    /// <returns>The mutated builder.</returns>
    private static StringBuilder ComposeCurrent(StringBuilder builder, string preamble)
    {
        if (builder.Length == 0)
        {
            builder.Append(preamble);

            return builder;
        }

        builder.Insert(0, _systemMessageSeparator);
        builder.Insert(0, preamble);

        return builder;
    }

    /// <summary>
    /// Adds exact output characteristics to a benchmark checksum.
    /// </summary>
    /// <param name="checksum">The current checksum.</param>
    /// <param name="value">The flushed system message.</param>
    /// <returns>The updated checksum.</returns>
    private static int AddToChecksum(int checksum, string value)
    {
        return HashCode.Combine(checksum, value.Length, value[0], value[^1]);
    }

    /// <summary>
    /// Creates a deterministic ASCII payload whose UTF-8 byte count equals its character count.
    /// </summary>
    /// <param name="length">The payload length.</param>
    /// <param name="value">The repeated ASCII character.</param>
    /// <returns>The generated payload.</returns>
    private static string CreatePayload(int length, char value)
    {
        return new string(value, length);
    }

    /// <summary>
    /// Counts the current chunks in a string builder.
    /// </summary>
    /// <param name="builder">The builder to inspect.</param>
    /// <returns>The number of chunks.</returns>
    private static int CountChunks(StringBuilder builder)
    {
        var count = 0;

        foreach (var chunk in builder.GetChunks())
        {
            _ = chunk;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Verifies exact output and builder identity for a composition candidate.
    /// </summary>
    /// <param name="expected">The exact expected output.</param>
    /// <param name="originalBuilder">The builder supplied to the candidate.</param>
    /// <param name="resultBuilder">The builder returned by the candidate.</param>
    private static void EnsureEquivalent(
        string expected,
        StringBuilder originalBuilder,
        StringBuilder resultBuilder)
    {
        if (!ReferenceEquals(originalBuilder, resultBuilder))
        {
            throw new InvalidOperationException("Composition replaced the system-message builder.");
        }

        if (!string.Equals(expected, resultBuilder.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Composition output differs from the captured implementation.");
        }
    }
}
