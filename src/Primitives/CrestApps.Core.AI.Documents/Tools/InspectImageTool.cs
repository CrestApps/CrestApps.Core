using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// System tool that performs on-demand visual inspection of an uploaded image.
/// The model calls this tool when text-based summaries are insufficient and
/// raw pixel-level understanding is required.
/// </summary>
public sealed class InspectImageTool : AIFunction
{
    public const string TheName = SystemToolNames.InspectImage;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "document_id": {
          "type": "string",
          "description": "The unique identifier of the image document to inspect."
        },
        "question": {
          "type": "string",
          "description": "An optional specific question about the image to focus the inspection on."
        }
      },
      "required": ["document_id"],
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
    public override string Description => "Performs a detailed visual inspection of an uploaded image. Use this when the text summary from read_document is insufficient and you need pixel-level analysis such as reading fine text, comparing visual elements, or understanding spatial layout.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes the image inspection logic.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<InspectImageTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("document_id", out var documentId))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'document_id'.", Name);

            return "Unable to find a 'document_id' argument in the arguments parameter.";
        }

        arguments.TryGetFirstString("question", out var question);

        var executionContext = AIInvocationScope.Current?.ToolExecutionContext;

        if (executionContext is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: execution context is missing.", Name);

            return "Image inspection requires an active execution context.";
        }

        var documentStore = arguments.Services.GetService<IAIDocumentStore>();

        if (documentStore is null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: document store is not available.", Name);

            return "Document store is not available.";
        }

        var document = await ResolveDocumentAsync(documentStore, documentId, executionContext, cancellationToken);

        if (document == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: document '{DocumentId}' was not found in this session.", Name, documentId);

            return $"Image document with ID '{documentId}' was not found in this session.";
        }

        if (!IsVisionImage(document))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: document '{DocumentId}' is not a vision image.", Name, documentId);

            return $"Document '{documentId}' is not an image. Use 'read_document' for non-image documents.";
        }

        var fileStore = arguments.Services.GetService<IDocumentFileStore>();

        if (fileStore is null || string.IsNullOrWhiteSpace(document.StoredFilePath))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: file store is not available or file path is missing.", Name);

            return "Image file is not available for inspection.";
        }

        var options = arguments.Services.GetRequiredService<IOptions<ChatDocumentsOptions>>().Value;

        if (options.MaxVisionImageBytesPerFile > 0 && document.FileSize > options.MaxVisionImageBytesPerFile)
        {
            logger.LogWarning(
                "AI tool '{ToolName}' failed: image '{DocumentId}' size ({FileSize} bytes) exceeds per-file limit of {MaxBytes} bytes.",
                Name,
                documentId,
                document.FileSize,
                options.MaxVisionImageBytesPerFile);

            return $"Image is too large for inspection ({document.FileSize} bytes exceeds the {options.MaxVisionImageBytesPerFile} byte limit).";
        }

        await using var stream = await fileStore.GetFileAsync(document.StoredFilePath);

        if (stream == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: image file not found at path '{Path}'.", Name, document.StoredFilePath);

            return "Image file could not be retrieved from storage.";
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        var imageBytes = memoryStream.ToArray();

        if (imageBytes.Length == 0)
        {
            return "Image file is empty.";
        }

        var deploymentManager = arguments.Services.GetRequiredService<IAIDeploymentManager>();

        var deployment = await deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Vision,
            cancellationToken: cancellationToken);

        if (deployment == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no vision-capable deployment available.", Name);

            return "No vision-capable deployment is available for image inspection.";
        }

        var clientFactory = arguments.Services.GetRequiredService<IAIClientFactory>();
        var chatClient = await clientFactory.CreateChatClientAsync(deployment);

        var contentType = document.ContentType ?? MediaTypeHelper.InferMediaType(Path.GetExtension(document.FileName));
        var userPrompt = string.IsNullOrWhiteSpace(question)
            ? $"Describe this image (\"{document.FileName}\") in detail. Include any text, layout, colors, and notable elements."
            : $"Regarding this image (\"{document.FileName}\"): {question}";

        var userContents = new List<AIContent>
        {
            new TextContent(userPrompt),
            new DataContent(imageBytes, contentType),
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a precise image analysis assistant. Answer the user's question about the provided image accurately and concisely."),
            new(ChatRole.User, userContents),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed for document '{DocumentId}'.", Name, documentId);
        }

        return response?.Text ?? "The vision model did not return a response.";
    }

    private static async Task<AIDocument> ResolveDocumentAsync(
        IAIDocumentStore documentStore,
        string documentId,
        AIToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var document = await documentStore.FindByIdAsync(documentId, cancellationToken);

        if (document == null)
        {
            return null;
        }

        if (executionContext.Resource is ChatInteraction interaction)
        {
            return document.ReferenceId == interaction.ItemId ? document : null;
        }

        if (executionContext.Resource is AIProfile profile)
        {
            if (document.ReferenceId == profile.ItemId)
            {
                return document;
            }

            if (AIInvocationScope.Current?.Items.TryGetValue(nameof(AIChatSession), out var sessionObj) == true &&
                sessionObj is AIChatSession session &&
                document.ReferenceId == session.SessionId)
            {
                return document;
            }
        }

        return null;
    }

    private static bool IsVisionImage(AIDocument document)
    {
        if (MediaTypeHelper.IsVisionImageMediaType(document.ContentType))
        {
            return true;
        }

        return MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(document.FileName));
    }
}
