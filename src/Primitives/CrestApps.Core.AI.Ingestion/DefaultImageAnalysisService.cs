using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Default implementation of <see cref="IImageAnalysisService"/> that sends the image
/// to a vision-capable chat model and parses the structured JSON analysis response.
/// </summary>
public sealed class DefaultImageAnalysisService : IImageAnalysisService
{
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ITemplateService _templateService;
    private readonly ILogger<DefaultImageAnalysisService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultImageAnalysisService"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager for resolving vision models.</param>
    /// <param name="clientFactory">The client factory for creating chat clients.</param>
    /// <param name="templateService">The template service for rendering the analysis prompt.</param>
    /// <param name="logger">The logger.</param>
    public DefaultImageAnalysisService(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ITemplateService templateService,
        ILogger<DefaultImageAnalysisService> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _templateService = templateService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ImageAnalysisResult> AnalyzeAsync(
        Stream imageStream,
        string contentType,
        string fileName,
        string chatDeploymentName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var content = await ReadStreamBytesAsync(imageStream, cancellationToken);

        return await AnalyzeAsync(
            new ImageAnalysisRequest
            {
                Content = content,
                ContentType = contentType,
                FileName = fileName,
                DeploymentName = chatDeploymentName,
                TemplateId = AITemplateIds.ImageAnalysis,
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ImageAnalysisResult> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        try
        {
            var deployment = await ResolveVisionDeploymentAsync(request.DeploymentName, cancellationToken);

            if (deployment == null)
            {
                _logger.LogWarning("No vision-capable deployment available for image analysis of '{FileName}'.", request.FileName);

                return ImageAnalysisResult.Failed("No vision-capable deployment is available for image analysis.");
            }

            var chatClient = await _clientFactory.CreateChatClientAsync(deployment);

            var messages = new List<ChatMessage>();

            var templateId = string.IsNullOrWhiteSpace(request.TemplateId)
                ? AITemplateIds.ImageAnalysis
                : request.TemplateId;

            var systemPrompt = await _templateService.RenderAsync(templateId, cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new(ChatRole.System, systemPrompt));
            }

            var userContents = new List<AIContent>
            {
                new TextContent(BuildUserPrompt(request)),
                new DataContent(request.Content, request.ContentType),
            };

            messages.Add(new(ChatRole.User, userContents));

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

            var rawText = response?.Text;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogWarning("Vision model returned empty response for image '{FileName}'.", request.FileName);

                return ImageAnalysisResult.Failed("The vision model returned an empty response.");
            }

            return ParseJsonAnalysisResponse(rawText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image analysis failed for '{FileName}'.", request.FileName);

            return ImageAnalysisResult.Failed($"Image analysis failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the message that travels with the image. The caption and the surrounding prose are what let a
    /// model tell an axis label from a stray number, and naming the language stops it translating a
    /// transcription that has to stay verbatim.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The user prompt.</returns>
    private static string BuildUserPrompt(ImageAnalysisRequest request)
    {
        var builder = new StringBuilder();

        builder.Append("Analyze this image: \"");
        builder.Append(request.FileName);
        builder.Append('"');

        if (!string.IsNullOrWhiteSpace(request.Caption))
        {
            builder.Append("\nCaption: ");
            builder.Append(request.Caption);
        }

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            builder.Append("\nSurrounding text: ");
            builder.Append(request.Context);
        }

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            builder.Append("\nThe figure is printed in '");
            builder.Append(request.Language);
            builder.Append("'. Transcribe in that language and never translate.");
        }

        return builder.ToString();
    }

    private async Task<AIDeployment> ResolveVisionDeploymentAsync(
        string chatDeploymentName,
        CancellationToken cancellationToken)
    {
        // Prioritize the global vision deployment.
        var visionDeployment = await _deploymentManager.ResolveSlotAsync(
            AIDeploymentSlotNames.Vision,
            cancellationToken: cancellationToken);

        if (visionDeployment != null)
        {
            return visionDeployment;
        }

        // Fall back to the specified chat deployment if it supports vision. Tested against the imageInput
        // capability rather than a routing flag, so a genuinely vision-capable model is not rejected merely
        // because nobody ticked the vision purpose for it.
        if (!string.IsNullOrWhiteSpace(chatDeploymentName))
        {
            var chatDeployment = await _deploymentManager.ResolveSlotAsync(
                AIDeploymentSlotNames.Chat,
                deploymentName: chatDeploymentName,
                cancellationToken: cancellationToken);

            if (chatDeployment is not null &&
                chatDeployment.TryGet<AIDeploymentMetadata>(out var metadata) &&
                metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput))
            {
                return chatDeployment;
            }
        }

        return null;
    }

    private ImageAnalysisResult ParseJsonAnalysisResponse(string rawText)
    {
        var json = JsonExtractor.ExtractJsonObject(rawText);

        if (json == null)
        {
            _logger.LogWarning("Vision model response did not contain a valid JSON object. Falling back to raw text.");

            return ImageAnalysisResult.Succeeded(
                caption: rawText.Length > 200 ? rawText[..200] : rawText,
                description: rawText,
                ocrText: string.Empty,
                detectedEntities: string.Empty,
                rawAnalysis: rawText);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var caption = GetStringProperty(root, "caption");
            var description = GetStringProperty(root, "description");
            var ocrText = GetStringProperty(root, "ocr_text");
            var detectedEntities = GetStringProperty(root, "detected_entities");

            return ImageAnalysisResult.Succeeded(caption, description, ocrText, detectedEntities, rawText);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse vision model JSON response. Falling back to raw text.");

            return ImageAnalysisResult.Succeeded(
                caption: rawText.Length > 200 ? rawText[..200] : rawText,
                description: rawText,
                ocrText: string.Empty,
                detectedEntities: string.Empty,
                rawAnalysis: rawText);
        }
    }

    private static string GetStringProperty(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task<byte[]> ReadStreamBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream ms)
        {
            return ms.ToArray();
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        return memoryStream.ToArray();
    }
}
