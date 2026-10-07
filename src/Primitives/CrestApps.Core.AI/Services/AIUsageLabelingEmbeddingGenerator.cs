using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Labels every request sent through an embedding generator with a usage category and purpose.
/// </summary>
/// <typeparam name="TInput">The embedding input type.</typeparam>
/// <typeparam name="TEmbedding">The embedding result type.</typeparam>
internal sealed class AIUsageLabelingEmbeddingGenerator<TInput, TEmbedding> : DelegatingEmbeddingGenerator<TInput, TEmbedding>
    where TEmbedding : Embedding
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingEmbeddingGenerator{TInput, TEmbedding}"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner generator.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingEmbeddingGenerator(
        IEmbeddingGenerator<TInput, TEmbedding> innerGenerator,
        string contextType,
        string purpose)
        : base(innerGenerator)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Generates embeddings with the request labeled.
    /// </summary>
    /// <param name="values">The values to embed.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(
        IEnumerable<TInput> values,
        EmbeddingGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var labeled = options?.Clone() ?? new EmbeddingGenerationOptions();
        labeled.AdditionalProperties = AIUsageLabels.Apply(labeled.AdditionalProperties, _contextType, _purpose);

        return base.GenerateAsync(values, labeled, cancellationToken);
    }
}
