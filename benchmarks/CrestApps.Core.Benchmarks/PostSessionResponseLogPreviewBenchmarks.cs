using System.Buffers;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured full-normalization response preview with the current implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PostSessionResponseLogPreviewBenchmarks
{
    private const int MaximumPreviewLength = 2_000;
    private const int MaximumDirectNormalizationLength = MaximumPreviewLength / 2;

    private static readonly MethodInfo _productionCreateResponseLogPreview = typeof(PostSessionProcessingService)
        .GetMethod("CreateResponseLogPreview", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the production response log preview helper.");

    private static readonly SearchValues<char> _responseLineEndings = SearchValues.Create("\r\n");

    private string _responseText;

    /// <summary>
    /// Gets or sets the ASCII response size in bytes.
    /// </summary>
    [Params(512, 2_048, 20_480, 1_048_576)]
    public int ResponseSize { get; set; }

    /// <summary>
    /// Gets or sets the response line-ending pattern.
    /// </summary>
    [Params("NoNewlines", "FrequentLf", "FrequentCrLf", "NewlinesNearBoundary")]
    public string ResponsePattern { get; set; }

    /// <summary>
    /// Creates the response and verifies exact legacy and current equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _responseText = CreateResponse(ResponseSize, ResponsePattern);

        var legacy = CreateResponseLogPreviewLegacy(_responseText);
        var current = CreateResponseLogPreviewCurrent(_responseText);
        var production = (string)_productionCreateResponseLogPreview.Invoke(null, [_responseText]);

        if (_responseText.Length != ResponseSize
            || !string.Equals(legacy, current, StringComparison.Ordinal)
            || !string.Equals(legacy, production, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The current response preview changed legacy formatting semantics.");
        }
    }

    /// <summary>
    /// Creates a preview by normalizing the complete response before truncation.
    /// </summary>
    /// <returns>The response log preview.</returns>
    [Benchmark(Baseline = true)]
    public string CreatePreviewLegacy()
    {
        return CreateResponseLogPreviewLegacy(_responseText);
    }

    /// <summary>
    /// Creates a preview with the current implementation.
    /// </summary>
    /// <returns>The response log preview.</returns>
    [Benchmark]
    public string CreatePreviewCurrent()
    {
        return CreateResponseLogPreviewCurrent(_responseText);
    }

    /// <summary>
    /// Creates an exact-size ASCII response with the requested line-ending pattern.
    /// </summary>
    /// <param name="responseSize">The response size.</param>
    /// <param name="responsePattern">The line-ending pattern.</param>
    /// <returns>The generated response.</returns>
    private static string CreateResponse(int responseSize, string responsePattern)
    {
        return string.Create(responseSize, responsePattern, static (characters, pattern) =>
        {
            characters.Fill('a');

            switch (pattern)
            {
                case "NoNewlines":
                    break;
                case "FrequentLf":
                    for (var index = 63; index < characters.Length; index += 64)
                    {
                        characters[index] = '\n';
                    }

                    break;
                case "FrequentCrLf":
                    for (var index = 62; index + 1 < characters.Length; index += 64)
                    {
                        characters[index] = '\r';
                        characters[index + 1] = '\n';
                    }

                    break;
                case "NewlinesNearBoundary":
                    var boundaryIndex = Math.Min(MaximumPreviewLength - 3, characters.Length - 4);
                    characters[boundaryIndex] = '\r';
                    characters[boundaryIndex + 2] = '\n';
                    break;
                default:
                    throw new InvalidOperationException($"Unknown response pattern '{pattern}'.");
            }
        });
    }

    /// <summary>
    /// Creates a preview with the captured complete replacement and truncation implementation.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <returns>The response log preview.</returns>
    private static string CreateResponseLogPreviewLegacy(string responseText)
    {
        if (string.IsNullOrEmpty(responseText))
        {
            return "(empty)";
        }

        var normalized = responseText
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return normalized.Length > MaximumPreviewLength
            ? normalized[..MaximumPreviewLength] + "..."
            : normalized;
    }

    /// <summary>
    /// Creates a preview with the current bounded escaping implementation.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <returns>The response log preview.</returns>
    private static string CreateResponseLogPreviewCurrent(string responseText)
    {
        if (string.IsNullOrEmpty(responseText))
        {
            return "(empty)";
        }

        if (responseText.Length <= MaximumDirectNormalizationLength)
        {
            return responseText
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }

        if (responseText.Length <= MaximumPreviewLength)
        {
            var lineEndingCount = responseText.AsSpan().CountAny(_responseLineEndings);

            if (lineEndingCount == 0)
            {
                return responseText;
            }

            var escapedLength = responseText.Length + lineEndingCount;

            if (escapedLength <= MaximumPreviewLength)
            {
                return string.Create(escapedLength, responseText, static (preview, response) =>
                    WriteEscapedResponse(response, preview));
            }

            return CreateTruncatedResponseLogPreview(responseText, true);
        }

        var requiresEscaping = responseText
            .AsSpan(0, MaximumPreviewLength)
            .IndexOfAny(_responseLineEndings) >= 0;

        return CreateTruncatedResponseLogPreview(responseText, requiresEscaping);
    }

    /// <summary>
    /// Creates a truncated response preview with an ellipsis.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <param name="requiresEscaping">Whether the retained prefix contains line endings.</param>
    /// <returns>The truncated response preview.</returns>
    private static string CreateTruncatedResponseLogPreview(
        string responseText,
        bool requiresEscaping)
    {
        return string.Create(
            MaximumPreviewLength + 3,
            (responseText, requiresEscaping),
            static (preview, state) =>
            {
                var content = preview[..MaximumPreviewLength];

                if (state.requiresEscaping)
                {
                    WriteEscapedResponse(state.responseText, content);
                }
                else
                {
                    state.responseText.AsSpan(0, MaximumPreviewLength).CopyTo(content);
                }

                preview[MaximumPreviewLength] = '.';
                preview[MaximumPreviewLength + 1] = '.';
                preview[MaximumPreviewLength + 2] = '.';
            });
    }

    /// <summary>
    /// Writes escaped response code units until the destination is full.
    /// </summary>
    /// <param name="responseText">The response text.</param>
    /// <param name="destination">The destination span.</param>
    private static void WriteEscapedResponse(string responseText, Span<char> destination)
    {
        var source = responseText.AsSpan();
        var sourceIndex = 0;
        var destinationIndex = 0;

        while (destinationIndex < destination.Length)
        {
            var remainingSource = source[sourceIndex..];
            var lineEndingIndex = remainingSource.IndexOfAny(_responseLineEndings);

            if (lineEndingIndex < 0)
            {
                remainingSource[..Math.Min(remainingSource.Length, destination.Length - destinationIndex)]
                    .CopyTo(destination[destinationIndex..]);

                return;
            }

            var copyLength = Math.Min(lineEndingIndex, destination.Length - destinationIndex);
            remainingSource[..copyLength].CopyTo(destination[destinationIndex..]);
            sourceIndex += copyLength;
            destinationIndex += copyLength;

            if (destinationIndex == destination.Length)
            {
                return;
            }

            var lineEnding = source[sourceIndex++];
            destination[destinationIndex++] = '\\';

            if (destinationIndex == destination.Length)
            {
                return;
            }

            destination[destinationIndex++] = lineEnding == '\r' ? 'r' : 'n';
        }
    }
}
