using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// System tool that generates images from text descriptions using DALL-E or compatible image generation models.
/// Returns markdown image syntax for inline rendering.
/// </summary>
public sealed class GenerateImageTool : AIFunction
{
    public const string TheName = SystemToolNames.GenerateImage;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "prompt": {
          "type": "string",
          "description": "A detailed description of the image to generate."
        }
      },
      "required": ["prompt"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Generates an image from a text description using an AI image generation model and returns it as markdown.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>()
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Invoke cores core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<GenerateImageTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("prompt", out var prompt))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'prompt'.", Name);

return "Unable to find a 'prompt' argument in the arguments parameter.";
        }

        try
        {
            var executionContext = AIInvocationScope.Current?.ToolExecutionContext;

            if (executionContext is null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: execution context is missing.", Name);

return $"Image generation is not available. The {nameof(AIToolExecutionContext)} is missing from the invocation context.";
            }

            var clientName = executionContext.ClientName;

            var deploymentManager = arguments.Services.GetRequiredService<IAIDeploymentManager>();
            var deployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Image, clientName, cancellationToken: cancellationToken);

            if (deployment == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no image model deployment configured.", Name);

return "Image generation is not available. No image model deployment is configured.";
            }

            var aIClientFactory = arguments.Services.GetRequiredService<IAIClientFactory>();

            var imageGenerator = await aIClientFactory.CreateImageGeneratorAsync(deployment);

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var options = new ImageGenerationOptions
            {
                ImageSize = new System.Drawing.Size(1024, 1024),
                ResponseFormat = ImageGenerationResponseFormat.Uri,
            };

            var request = new ImageGenerationRequest
            {
                Prompt = prompt,
            };
#pragma warning restore MEAI001

            var result = await imageGenerator.GenerateAsync(request, options, cancellationToken);

            if (result?.Contents is null || result.Contents.Count == 0)
            {
                logger.LogWarning("AI tool '{ToolName}' returned no images.", Name);

return "No images were generated.";
            }

            using var builder = ZString.CreateStringBuilder();

            foreach (var contentItem in result.Contents)
            {
                var imageUri = ExtractImageUri(contentItem);

                if (!string.IsNullOrWhiteSpace(imageUri))
                {
                    builder.Append("![Generated Image](");
                    builder.Append(imageUri);
                    builder.AppendLine(")");
                    builder.AppendLine();
                }
            }

            if (builder.Length == 0)
            {
                logger.LogWarning("AI tool '{ToolName}' generated no usable image URIs.", Name);

                return "No images were generated.";
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return builder.ToString();
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Image generation is not supported by the configured provider.");

            return "Image generation is not supported by the configured AI provider.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during image generation.");

return "An error occurred while generating the image.";
        }
    }

    private static string ExtractImageUri(AIContent contentItem)
    {
        if (contentItem is UriContent uriContent)
        {
            return uriContent.Uri?.ToString();
        }

        if (contentItem is DataContent dataContent && dataContent.Uri is not null)
        {
            return dataContent.Uri.ToString();
        }

        return null;
    }
}
