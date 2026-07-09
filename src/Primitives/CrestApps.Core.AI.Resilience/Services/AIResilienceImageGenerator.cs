#pragma warning disable MEAI001
using Microsoft.Extensions.AI;
using Polly;

namespace CrestApps.Core.AI.Resilience.Services;

/// <summary>
/// Applies a Polly resilience pipeline to image-generation requests.
/// </summary>
internal sealed class AIResilienceImageGenerator : IImageGenerator
{
    private readonly IImageGenerator _innerGenerator;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIResilienceImageGenerator"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner image generator.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    public AIResilienceImageGenerator(
        IImageGenerator innerGenerator,
        ResiliencePipeline pipeline)
    {
        _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// Generates images through the configured resilience pipeline.
    /// </summary>
    /// <param name="request">The image-generation request.</param>
    /// <param name="options">The image-generation options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image-generation response.</returns>
    public Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _pipeline.ExecuteAsync(
            static async (state, token) => await state.InnerGenerator.GenerateAsync(state.Request, state.Options, token),
            (InnerGenerator: _innerGenerator, Request: request, Options: options),
            cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Gets a service exposed by the inner image generator.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The optional service key.</param>
    /// <returns>The resolved service, if any.</returns>
    public object GetService(Type serviceType, object serviceKey)
    {
        return _innerGenerator.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes the inner image generator.
    /// </summary>
    public void Dispose()
    {
        _innerGenerator.Dispose();
    }
}
#pragma warning restore MEAI001
