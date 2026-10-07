using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Tells the primary model which PDFs the conversation has — the uploaded ones and the ones the PDF agent made —
/// and that work on them, as opposed to questions about what they say, belongs to the PDF agent.
/// </summary>
/// <remarks>
/// An uploaded PDF is also indexed as an ordinary document, so the model can already search and read it. What
/// it cannot do from there is change it, show it, or read its tables, forms and layout, and without being
/// told it answers such a request by describing the file instead of delegating. A PDF the agent made is not
/// an upload at all, and without being told about it the model answered "make the title green" by writing a
/// new, plain file of its own with generate_file.
/// </remarks>
internal sealed class PdfDocumentOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// The identifier of the template appended to the system message.
    /// </summary>
    public const string TemplateId = "pdf-document-availability";

    private readonly ITemplateService _templateService;
    private readonly IPdfWorkspaceStore _workspaceStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    /// <param name="workspaceStore">The store of the PDFs the agent keeps for each conversation.</param>
    public PdfDocumentOrchestrationHandler(
        ITemplateService templateService,
        IPdfWorkspaceStore workspaceStore)
    {
        _templateService = templateService;
        _workspaceStore = workspaceStore;
    }

    /// <summary>
    /// Runs before the context is built. Nothing is needed here.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Appends the PDF guidance when the conversation holds uploaded PDFs.
    /// </summary>
    /// <param name="context">The built context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The agent's own orchestration must not be told to delegate to itself.
        if (AIInvocationScope.Current?.AgentInvocationDepth > 0 || context.OrchestrationContext?.CompletionContext is null)
        {
            return;
        }

        IEnumerable<ChatDocumentInfo> documents = null;
        PdfWorkspaceScope? scope = null;

        if (context.Resource is ChatInteraction interaction)
        {
            documents = interaction.Documents;
            scope = new PdfWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction);
        }
        else if (context.Resource is AIProfile &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties is not null &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue("Session", out var sessionObject) &&
            sessionObject is AIChatSession session)
        {
            documents = session.Documents;

            if (!string.IsNullOrEmpty(session.SessionId))
            {
                scope = new PdfWorkspaceScope(session.SessionId, AIReferenceTypes.Document.ChatSession);
            }
        }

        var pdfs = documents?
            .Where(document => document is not null && string.Equals(Path.GetExtension(document.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        // A finished file is how its document reads, not a PDF of its own to name.
        var working = scope is { } workspace && _workspaceStore is not null
            ? (await _workspaceStore.LoadAsync(workspace, cancellationToken)).Documents
                .Where(document => string.IsNullOrEmpty(document.FinishedFrom))
                .Select(document => new Dictionary<string, object>
                {
                    ["Name"] = document.Name,
                    ["PageCount"] = document.PageCount,
                })
                .ToArray()
            : [];

        if (pdfs.Length == 0 && working.Length == 0)
        {
            return;
        }

        var text = await _templateService.RenderAsync(
            TemplateId,
            new Dictionary<string, object>
            {
                ["pdfDocuments"] = pdfs,
                ["workingDocuments"] = working,
                ["pdfAgentName"] = PdfAgentProvider.AgentName,
                ["isRealtime"] = context.OrchestrationContext.ExecutionMode == OrchestrationExecutionMode.Realtime,
            },
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(text))
        {
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.Append(text);
        }
    }
}
