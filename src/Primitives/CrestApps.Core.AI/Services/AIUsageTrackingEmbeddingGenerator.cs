using System.Diagnostics;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Meters every embedding request made through an embedding generator the AI client factory created, whatever
/// the provider.
/// </summary>
internal sealed class AIUsageTrackingEmbeddingGenerator : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingEmbeddingGenerator"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner generator.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AIUsageTrackingEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> innerGenerator,
        AIUsageRecorder recorder)
        : base(innerGenerator)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Generates embeddings and records the tokens the provider reported for them.
    /// </summary>
    /// <param name="values">The values to embed.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var embeddings = await base.GenerateAsync(values, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        if (embeddings is not null)
        {
            await _recorder.RecordAsync(
                AIUsageOperationTypes.Embedding,
                options?.AdditionalProperties,
                embeddings.FirstOrDefault()?.ModelId,
                null,
                embeddings.Usage,
                stopwatch.Elapsed.TotalMilliseconds,
                false,
                null,
                cancellationToken);
        }

        return embeddings;
    }
}
