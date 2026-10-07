using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Labels every request sent through an image generator with a usage category and purpose.
/// </summary>
internal sealed class AIUsageLabelingImageGenerator : DelegatingImageGenerator
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingImageGenerator"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner generator.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingImageGenerator(
        IImageGenerator innerGenerator,
        string contextType,
        string purpose)
        : base(innerGenerator)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Generates images with the request labeled.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var labeled = options?.Clone() ?? new ImageGenerationOptions();
        labeled.AdditionalProperties = AIUsageLabels.Apply(labeled.AdditionalProperties, _contextType, _purpose);

        return base.GenerateAsync(request, labeled, cancellationToken);
    }
}
#pragma warning restore MEAI001 // This client type is for evaluation purposes only and may change in future updates.
