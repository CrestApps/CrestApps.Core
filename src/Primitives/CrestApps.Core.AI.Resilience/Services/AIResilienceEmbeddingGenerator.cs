#pragma warning disable MEAI001
using Microsoft.Extensions.AI;
using Polly;

namespace CrestApps.Core.AI.Resilience.Services;

/// <summary>
/// Applies a Polly resilience pipeline to embedding-generation requests.
/// </summary>
internal sealed class AIResilienceEmbeddingGenerator<TInput, TEmbedding> : IEmbeddingGenerator<TInput, TEmbedding>
    where TEmbedding : Embedding
{
    private readonly IEmbeddingGenerator<TInput, TEmbedding> _innerGenerator;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIResilienceEmbeddingGenerator{TInput, TEmbedding}"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner embedding generator.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    public AIResilienceEmbeddingGenerator(
        IEmbeddingGenerator<TInput, TEmbedding> innerGenerator,
        ResiliencePipeline pipeline)
    {
        _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// Generates embeddings through the configured resilience pipeline.
    /// </summary>
    /// <param name="values">The input values.</param>
    /// <param name="options">The embedding options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated embeddings.</returns>
    public Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(
        IEnumerable<TInput> values,
        EmbeddingGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var requestValues = values as IReadOnlyList<TInput> ?? values.ToList();

        return _pipeline.ExecuteAsync(
            static async (state, token) => await state.InnerGenerator.GenerateAsync(state.Values, state.Options, token),
            (InnerGenerator: _innerGenerator, Values: requestValues, Options: options),
            cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Gets a service exposed by the inner embedding generator.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The optional service key.</param>
    /// <returns>The resolved service, if any.</returns>
    public object GetService(Type serviceType, object serviceKey)
    {
        return _innerGenerator.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes the inner embedding generator.
    /// </summary>
    public void Dispose()
    {
        _innerGenerator.Dispose();
    }
}
#pragma warning restore MEAI001
