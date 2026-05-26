using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Orchestration context handler that populates document references
/// from a <see cref="ChatInteraction"/> or <see cref="AIProfile"/> resource
/// and enriches the system message with document metadata so the model knows
/// which documents are available and which tools to use to access them.
/// </summary>
/// <remarks>
/// Document processing tools are registered as system tools and are always included
/// by the orchestrator. This handler provides the model with document metadata
/// and tool descriptions. The resource ID is resolved server-side from
/// <see cref="AIToolExecutionContext.Resource"/> - it is never exposed to the model.
/// </remarks>
public sealed class DocumentOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    private readonly AIToolDefinitionOptions _toolDefinitions;
    private readonly ChatDocumentsOptions _documentOptions;
    private readonly ITemplateService _templateService;
    private readonly IAIDocumentStore _documentStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ILogger<DocumentOrchestrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="toolDefinitions">The tool definitions.</param>
    /// <param name="documentOptions">The document options.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="documentStore">The document store.</param>
    /// <param name="fileStore">The document file store.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="logger">The logger.</param>
    public DocumentOrchestrationHandler(
        IOptions<AIToolDefinitionOptions> toolDefinitions,
        IOptions<ChatDocumentsOptions> documentOptions,
        ITemplateService templateService,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        IAIDeploymentManager deploymentManager,
        ILogger<DocumentOrchestrationHandler> logger)
    {
        _toolDefinitions = toolDefinitions.Value;
        _documentOptions = documentOptions.Value;
        _templateService = templateService;
        _documentStore = documentStore;
        _fileStore = fileStore;
        _deploymentManager = deploymentManager;
        _logger = logger;
    }

    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        if (context.Resource is ChatInteraction interaction &&
            interaction.Documents is { Count: > 0 })
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Populating {DocCount} document(s) from ChatInteraction '{ItemId}' into orchestration context.",
                interaction.Documents.Count, interaction.ItemId);
            }

            context.Context.Documents ??= [];
            context.Context.Documents.AddRange(interaction.Documents);
        }
        else if (context.Resource is AIProfile profile)
        {
            if (profile.TryGet<DocumentsMetadata>(out var documentsMetadata) && documentsMetadata.Documents is { Count: > 0 })
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Populating {DocCount} document(s) from AIProfile '{ProfileId}' into orchestration context.",
                    documentsMetadata.Documents.Count, profile.ItemId);
                }

                context.Context.Documents ??= [];
                context.Context.Documents.AddRange(documentsMetadata.Documents);
            }
            else if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("AIProfile '{ProfileId}' has no documents attached.", profile.ItemId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        IEnumerable<ChatDocumentInfo> knowledgeBaseDocuments = null;
        IEnumerable<ChatDocumentInfo> userSuppliedDocuments = null;

        if (context.Resource is ChatInteraction interaction && interaction.Documents is { Count: > 0 })
        {
            userSuppliedDocuments = interaction.Documents;
        }
        else if (context.Resource is AIProfile profile)
        {
            knowledgeBaseDocuments = profile.TryGet<DocumentsMetadata>(out var documentsMetadata)
                ? documentsMetadata.Documents
                : null;

            if (context.OrchestrationContext.CompletionContext?.AdditionalProperties is not null &&
                context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue("Session", out var sessionObj) &&
                    sessionObj is AIChatSession session &&
                        session.Documents is { Count: > 0 })
            {
                userSuppliedDocuments = session.Documents;
            }
        }

        var ragMetadata = GetRagMetadata(context.Resource);

        var hasKnowledgeBaseDocuments = knowledgeBaseDocuments?.Any() == true;
        var hasUserSuppliedDocuments = userSuppliedDocuments?.Any() == true;

        if (!hasKnowledgeBaseDocuments && !hasUserSuppliedDocuments &&
            context.OrchestrationContext.Documents is { Count: > 0 } existingDocuments)
        {
            var clonedDocuments = existingDocuments.ToArray();

            if (context.Resource is AIProfile)
            {
                knowledgeBaseDocuments = clonedDocuments;
                hasKnowledgeBaseDocuments = true;
            }
            else
            {
                userSuppliedDocuments = clonedDocuments;
                hasUserSuppliedDocuments = true;
            }
        }

        if ((!hasKnowledgeBaseDocuments && !hasUserSuppliedDocuments) || context.OrchestrationContext.CompletionContext is null)
        {
            return;
        }

        var visionContentResult = await BuildVisionUserContentsAsync(context, userSuppliedDocuments, cancellationToken);

        var searchableUserSuppliedDocuments = userSuppliedDocuments?
            .Where(document => !IsVisionDocument(document))
            .ToArray();

        var visionUserSuppliedDocuments = userSuppliedDocuments?
            .Where(document => IsVisionDocument(document) && visionContentResult.IncludedDocumentIds.Contains(document.DocumentId))
            .ToArray();

        context.OrchestrationContext.Documents ??= [];
        context.OrchestrationContext.Documents.Clear();

        if (hasKnowledgeBaseDocuments)
        {
            context.OrchestrationContext.Documents.AddRange(knowledgeBaseDocuments);
        }

        if (hasUserSuppliedDocuments)
        {
            context.OrchestrationContext.Documents.AddRange(userSuppliedDocuments);
        }

        // Signal document availability so system tools (e.g., search_documents)
        // are included in the tool registry for this completion context.
        context.OrchestrationContext.CompletionContext.AdditionalProperties[AICompletionContextKeys.HasDocuments] = true;

        // Discover document processing tools dynamically by purpose
        // to list their descriptions in the system message.
        var docTools = _toolDefinitions.Tools
            .Where(t => t.Value.HasPurpose(AIToolPurposes.DocumentProcessing))
            .Select(t => t.Value)
            .ToList();

        var arguments = new Dictionary<string, object>
        {
            ["tools"] = docTools,
            ["availableDocuments"] = context.OrchestrationContext.Documents,
            ["knowledgeBaseDocuments"] = hasKnowledgeBaseDocuments ? knowledgeBaseDocuments : [],
            ["userSuppliedDocuments"] = searchableUserSuppliedDocuments ?? [],
            ["visionUserSuppliedDocuments"] = visionUserSuppliedDocuments ?? [],
            ["isInScope"] = ragMetadata?.IsInScope == true,
        };

        var header = await _templateService.RenderAsync(AITemplateIds.DocumentAvailability, arguments, cancellationToken);

        if (!string.IsNullOrEmpty(header))
        {
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.Append(header);
        }

        if (visionContentResult.Contents.Count > 0)
        {
            context.OrchestrationContext.Properties[OrchestrationPropertyKeys.VisionUserContents] = visionContentResult.Contents;
        }
    }

    private static AIDataSourceRagMetadata GetRagMetadata(object resource)
    {
        if (resource is AIProfile profile &&
            profile.TryGet<AIDataSourceRagMetadata>(out var profileMetadata))
        {
            return profileMetadata;
        }

        if (resource is ChatInteraction interaction &&
            interaction.TryGet<AIDataSourceRagMetadata>(out var interactionMetadata))
        {
            return interactionMetadata;
        }

        return null;
    }

    private async Task<VisionUserContentResult> BuildVisionUserContentsAsync(
        OrchestrationContextBuiltContext context,
        IEnumerable<ChatDocumentInfo> userSuppliedDocuments,
        CancellationToken cancellationToken)
    {
        if (userSuppliedDocuments?.Any() != true)
        {
            return VisionUserContentResult.Empty;
        }

        var deployment = await ResolveChatDeploymentAsync(context, cancellationToken);

        if (deployment?.Purpose.Supports(AIDeploymentPurpose.Vision) != true)
        {
            return VisionUserContentResult.Empty;
        }

        var session = context.OrchestrationContext.CompletionContext?.AdditionalProperties is not null
            && context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue(AICompletionContextKeys.Session, out var sessionObject)
            ? sessionObject as AIChatSession
            : null;

        var reference = GetVisionDocumentReference(context.Resource, session);

        if (reference == null)
        {
            return VisionUserContentResult.Empty;
        }

        var visionDocuments = await _documentStore.GetDocumentsAsync(reference.Value.ReferenceId, reference.Value.ReferenceType);
        var documentIds = new HashSet<string>(
            userSuppliedDocuments
                .Where(document => MediaTypeHelper.IsVisionImageMediaType(document.ContentType) || MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(document.FileName)))
                .Select(document => document.DocumentId),
            StringComparer.OrdinalIgnoreCase);

        if (documentIds.Count == 0)
        {
            return VisionUserContentResult.Empty;
        }

        var remainingBytes = _documentOptions.MaxVisionInputBytesPerRequest > 0
            ? _documentOptions.MaxVisionInputBytesPerRequest
            : long.MaxValue;

        var contents = new List<AIContent>();
        var includedDocumentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in visionDocuments.Where(document => documentIds.Contains(document.ItemId)))
        {
            if (string.IsNullOrWhiteSpace(document.StoredFilePath))
            {
                continue;
            }

            if (ShouldSkipVisionDocument(document, remainingBytes))
            {
                continue;
            }

            await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

            if (stream == null)
            {
                continue;
            }

            var data = await ReadVisionDocumentBytesAsync(document, stream, cancellationToken);

            if (data == null || data.Length == 0)
            {
                continue;
            }

            contents.Add(new DataContent(data, document.ContentType ?? MediaTypeHelper.InferMediaType(Path.GetExtension(document.FileName))));
            includedDocumentIds.Add(document.ItemId);
            remainingBytes -= data.Length;
        }

        return new VisionUserContentResult(contents, includedDocumentIds);
    }

    private async Task<AIDeployment> ResolveChatDeploymentAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken)
    {
        return await _deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Chat,
            deploymentName: context.OrchestrationContext.CompletionContext?.ChatDeploymentName,
            cancellationToken: cancellationToken);
    }

    private static bool IsVisionDocument(ChatDocumentInfo document)
    {
        if (document == null)
        {
            return false;
        }

        if (MediaTypeHelper.IsVisionImageMediaType(document.ContentType))
        {
            return true;
        }

        return MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(document.FileName));
    }

    private static (string ReferenceId, string ReferenceType)? GetVisionDocumentReference(object resource, AIChatSession session)
    {
        return resource switch
        {
            ChatInteraction interaction => (interaction.ItemId, AIReferenceTypes.Document.ChatInteraction),
            AIProfile when session != null => (session.SessionId, AIReferenceTypes.Document.ChatSession),
            _ => null,
        };
    }

    private bool ShouldSkipVisionDocument(AIDocument document, long remainingBytes)
    {
        if (document.FileSize <= 0)
        {
            _logger.LogWarning(
                "Skipping vision document '{DocumentId}' because its file size metadata is missing or invalid.",
                document.ItemId);

            return true;
        }

        if (document.FileSize > int.MaxValue)
        {
            _logger.LogWarning(
                "Skipping vision document '{DocumentId}' because its size ({FileSize} bytes) exceeds the supported in-memory limit.",
                document.ItemId,
                document.FileSize);

            return true;
        }

        if (_documentOptions.MaxVisionImageBytesPerFile > 0 && document.FileSize > _documentOptions.MaxVisionImageBytesPerFile)
        {
            _logger.LogWarning(
                "Skipping vision document '{DocumentId}' because its size ({FileSize} bytes) exceeds the per-file limit of {MaxBytesPerFile} bytes.",
                document.ItemId,
                document.FileSize,
                _documentOptions.MaxVisionImageBytesPerFile);

            return true;
        }

        if (document.FileSize > remainingBytes)
        {
            _logger.LogWarning(
                "Skipping vision document '{DocumentId}' because it would exceed the configured multimodal image budget of {MaxBytes} bytes for a single request.",
                document.ItemId,
                _documentOptions.MaxVisionInputBytesPerRequest);

            return true;
        }

        return false;
    }

    private static async Task<byte[]> ReadVisionDocumentBytesAsync(
        AIDocument document,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var data = GC.AllocateUninitializedArray<byte>((int)document.FileSize);
        var totalRead = 0;

        while (totalRead < data.Length)
        {
            var bytesRead = await stream.ReadAsync(data.AsMemory(totalRead), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        if (totalRead == 0)
        {
            return null;
        }

        if (totalRead == data.Length)
        {
            return data;
        }

        return data[..totalRead];
    }

    private sealed class VisionUserContentResult
    {
        public static readonly VisionUserContentResult Empty = new([], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public VisionUserContentResult(
            IReadOnlyList<AIContent> contents,
            IReadOnlySet<string> includedDocumentIds)
        {
            Contents = contents;
            IncludedDocumentIds = includedDocumentIds;
        }

        public IReadOnlyList<AIContent> Contents { get; }

        public IReadOnlySet<string> IncludedDocumentIds { get; }
    }
}
