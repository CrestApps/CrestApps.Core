using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Generates a picture for a slide with the host's image model.
/// </summary>
/// <remarks>
/// The picture is asked for as data rather than as an address, so it is placed in the deck without this host
/// fetching anything from the network on a model's say-so. An address is only followed when the image service
/// itself returned one.
/// </remarks>
internal static class PresentationImageGenerator
{
    private const long MaxDownloadBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Generates a picture.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="prompt">What the picture should show.</param>
    /// <param name="shape">The picture's shape: <c>landscape</c>, <c>portrait</c> or <c>square</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picture, or an explanation of why there is none.</returns>
    public static async Task<(PresentationImageData Image, string Error)> GenerateAsync(IServiceProvider services, string prompt, string shape, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return (null, "Describe the picture to generate.");
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(PresentationImageGenerator).FullName);
        var clientName = AIInvocationScope.Current?.ToolExecutionContext?.ClientName;
        var deploymentManager = services.GetService<IAIDeploymentManager>();
        var clientFactory = services.GetService<IAIClientFactory>();

        if (deploymentManager is null || clientFactory is null)
        {
            return (null, "Image generation is not available on this host.");
        }

        var deployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Image, clientName: clientName, cancellationToken: cancellationToken);

        if (deployment is null)
        {
            return (null, "Image generation is not available: no image model deployment is configured.");
        }

        try
        {
            var generator = await clientFactory.CreateImageGeneratorAsync(deployment);
            var size = shape?.Trim().ToLowerInvariant() switch
            {
                "portrait" or "tall" => new System.Drawing.Size(1024, 1536),
                "square" => new System.Drawing.Size(1024, 1024),
                _ => new System.Drawing.Size(1536, 1024),
            };

#pragma warning disable MEAI001 // Image generation is marked experimental in Microsoft.Extensions.AI.
            ImageGenerationResponse response;

            try
            {
                response = await generator.GenerateAsync(new ImageGenerationRequest { Prompt = prompt }, new ImageGenerationOptions { ImageSize = size, ResponseFormat = ImageGenerationResponseFormat.Data }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && size.Width != size.Height)
            {
                // Not every image model takes a wide canvas; a square picture still makes a slide image.
                if (logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    logger.LogDebug(exception, "The image model refused a {Width}x{Height} picture; retrying square.", size.Width, size.Height);
                }

                response = await generator.GenerateAsync(new ImageGenerationRequest { Prompt = prompt }, new ImageGenerationOptions { ImageSize = new System.Drawing.Size(1024, 1024), ResponseFormat = ImageGenerationResponseFormat.Data }, cancellationToken);
            }
#pragma warning restore MEAI001

            foreach (var content in response?.Contents ?? [])
            {
                var data = content switch
                {
                    DataContent dataContent => dataContent.Data.ToArray(),
                    UriContent uri when uri.Uri is { Scheme: "https" or "http" } address => await DownloadAsync(services, address, cancellationToken),
                    _ => null,
                };

                if (data is { Length: > 0 } && PresentationImageInfo.TryRead(data, out var contentType, out var width, out var height))
                {
                    return (new PresentationImageData
                    {
                        Data = data,
                        ContentType = contentType,
                        FileName = "Generated image",
                        PixelWidth = width,
                        PixelHeight = height,
                    }, null);
                }
            }

            return (null, "The image model returned no picture that can be placed on a slide.");
        }
        catch (NotSupportedException)
        {
            return (null, "The configured AI provider cannot generate images.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Generating a slide image failed.");

            return (null, "The picture could not be generated: " + exception.Message);
        }
    }

    private static async Task<byte[]> DownloadAsync(IServiceProvider services, Uri address, CancellationToken cancellationToken)
    {
        var factory = services.GetService<IHttpClientFactory>();
        using var client = factory?.CreateClient(nameof(PresentationImageGenerator)) ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > MaxDownloadBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}
