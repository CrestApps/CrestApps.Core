using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Services;

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

        try
        {
            var deployment = await ResolveVisionDeploymentAsync(chatDeploymentName, cancellationToken);

            if (deployment == null)
            {
                _logger.LogWarning("No vision-capable deployment available for image analysis of '{FileName}'.", fileName);

                return ImageAnalysisResult.Failed("No vision-capable deployment is available for image analysis.");
            }

            var chatClient = await _clientFactory.CreateChatClientAsync(deployment);

            var messages = new List<ChatMessage>();

            var systemPrompt = await _templateService.RenderAsync(AITemplateIds.ImageAnalysis, cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new(ChatRole.System, systemPrompt));
            }

            var imageBytes = await ReadStreamBytesAsync(imageStream, cancellationToken);

            var userContents = new List<AIContent>
            {
                new TextContent($"Analyze this image: \"{fileName}\""),
                new DataContent(imageBytes, contentType),
            };

            messages.Add(new(ChatRole.User, userContents));

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

            var rawText = response?.Text;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogWarning("Vision model returned empty response for image '{FileName}'.", fileName);

                return ImageAnalysisResult.Failed("The vision model returned an empty response.");
            }

            return ParseJsonAnalysisResponse(rawText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image analysis failed for '{FileName}'.", fileName);

            return ImageAnalysisResult.Failed($"Image analysis failed: {ex.Message}");
        }
    }

    private async Task<AIDeployment> ResolveVisionDeploymentAsync(
        string chatDeploymentName,
        CancellationToken cancellationToken)
    {
        // Prioritize the global vision deployment.
        var visionDeployment = await _deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Vision,
            cancellationToken: cancellationToken);

        if (visionDeployment != null)
        {
            return visionDeployment;
        }

        // Fall back to the specified chat deployment if it supports vision.
        if (!string.IsNullOrWhiteSpace(chatDeploymentName))
        {
            var chatDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Chat,
                deploymentName: chatDeploymentName,
                cancellationToken: cancellationToken);

            if (chatDeployment?.Purpose.Supports(AIDeploymentPurpose.Vision) == true)
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
