using System.Diagnostics;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
/// <summary>
/// Meters every image request made through an image generator the AI client factory created, whatever the
/// provider. Both the images produced and any tokens reported are recorded, because models bill one or the other.
/// </summary>
internal sealed class AIUsageTrackingImageGenerator : DelegatingImageGenerator
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingImageGenerator"/> class.
    /// </summary>
    /// <param name="innerGenerator">The inner generator.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AIUsageTrackingImageGenerator(
        IImageGenerator innerGenerator,
        AIUsageRecorder recorder)
        : base(innerGenerator)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Generates images and records how many were produced and the tokens the provider reported.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GenerateAsync(request, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        if (response is not null)
        {
            var imageCount = response.Contents?.Count(content => content is DataContent or UriContent) ?? 0;

            await _recorder.RecordAsync(
                AIUsageOperationTypes.Image,
                options?.AdditionalProperties,
                null,
                null,
                response.Usage,
                stopwatch.Elapsed.TotalMilliseconds,
                false,
                record => record.ImageCount = record.ImageCount > 0 ? record.ImageCount : imageCount,
                cancellationToken);
        }

        return response;
    }
}
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
