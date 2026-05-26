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
    private const string VisionUserContentsKey = "VisionUserContents";

    private readonly AIToolDefinitionOptions _toolDefinitions;
    private readonly ITemplateService _templateService;
    private readonly IAIDocumentStore _documentStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ILogger<DocumentOrchestrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="toolDefinitions">The tool definitions.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="logger">The logger.</param>
    public DocumentOrchestrationHandler(
        IOptions<AIToolDefinitionOptions> toolDefinitions,
        ITemplateService templateService,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        IAIDeploymentManager deploymentManager,
        ILogger<DocumentOrchestrationHandler> logger)
    {
        _toolDefinitions = toolDefinitions.Value;
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

        var searchableUserSuppliedDocuments = userSuppliedDocuments?
            .Where(document => !IsVisionDocument(document))
            .ToArray();
        var visionUserSuppliedDocuments = userSuppliedDocuments?
            .Where(IsVisionDocument)
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
            ["knowledgeBaseDocuments"] = hasKnowledgeBaseDocuments ? knowledgeBaseDocuments : Array.Empty<ChatDocumentInfo>(),
            ["userSuppliedDocuments"] = searchableUserSuppliedDocuments ?? Array.Empty<ChatDocumentInfo>(),
            ["visionUserSuppliedDocuments"] = visionUserSuppliedDocuments ?? Array.Empty<ChatDocumentInfo>(),
            ["isInScope"] = ragMetadata?.IsInScope == true,
        };

        var header = await _templateService.RenderAsync(AITemplateIds.DocumentAvailability, arguments, cancellationToken);

        if (!string.IsNullOrEmpty(header))
        {
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.Append(header);
        }

        var visionContents = await BuildVisionUserContentsAsync(context, userSuppliedDocuments, cancellationToken);

        if (visionContents.Count > 0)
        {
            context.OrchestrationContext.Properties[VisionUserContentsKey] = visionContents;
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

    private async Task<List<AIContent>> BuildVisionUserContentsAsync(
        OrchestrationContextBuiltContext context,
        IEnumerable<ChatDocumentInfo> userSuppliedDocuments,
        CancellationToken cancellationToken)
    {
        if (userSuppliedDocuments?.Any() != true)
        {
            return [];
        }

        var deployment = await ResolveChatDeploymentAsync(context, cancellationToken);

        if (deployment?.Purpose.Supports(AIDeploymentPurpose.Vision) != true)
        {
            return [];
        }

        var session = context.OrchestrationContext.CompletionContext?.AdditionalProperties is not null
            && context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue(AICompletionContextKeys.Session, out var sessionObject)
            ? sessionObject as AIChatSession
            : null;

        var reference = GetVisionDocumentReference(context.Resource, session);

        if (reference == null)
        {
            return [];
        }

        var visionDocuments = await _documentStore.GetDocumentsAsync(reference.Value.ReferenceId, reference.Value.ReferenceType);
        var documentIds = new HashSet<string>(
            userSuppliedDocuments
                .Where(document => MediaTypeHelper.IsVisionImageMediaType(document.ContentType) || MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(document.FileName)))
                .Select(document => document.DocumentId),
            StringComparer.OrdinalIgnoreCase);

        if (documentIds.Count == 0)
        {
            return [];
        }

        var contents = new List<AIContent>();

        foreach (var document in visionDocuments.Where(document => documentIds.Contains(document.ItemId)))
        {
            if (string.IsNullOrWhiteSpace(document.StoredFilePath))
            {
                continue;
            }

            await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

            if (stream == null)
            {
                continue;
            }

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);

            if (memoryStream.Length == 0)
            {
                continue;
            }

            contents.Add(new DataContent(memoryStream.ToArray(), document.ContentType ?? MediaTypeHelper.InferMediaType(Path.GetExtension(document.FileName))));
        }

        return contents;
    }

    private async Task<AIDeployment> ResolveChatDeploymentAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken)
    {
        var completionContext = context.OrchestrationContext.CompletionContext;
        var deploymentName = completionContext?.ChatDeploymentName;

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            deploymentName = context.Resource switch
            {
                ChatInteraction interaction => interaction.ChatDeploymentName,
                AIProfile profile => profile.ChatDeploymentName,
                _ => null,
            };
        }

        return await _deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Chat, deploymentName: deploymentName, cancellationToken: cancellationToken);
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
}
