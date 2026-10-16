using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Tells the primary model that making a PDF belongs to the PDF agent, which PDFs the conversation has — the
/// uploaded ones and the ones the agent made — and that work on them, as opposed to questions about what they
/// say, belongs to the agent too.
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

    // The tools every PDF request may need whatever it is about: finding the documents, building one, showing
    // it and delivering it.
    private static readonly string[] _coreAgentToolNames =
    [
        PdfToolNames.GetPdfInfo,
        PdfToolNames.CreatePdf,
        PdfToolNames.AddPdfContent,
        PdfToolNames.FormatPdf,
        PdfToolNames.PreviewPdf,
        PdfToolNames.ExportPdf,
        PdfToolNames.ConvertToPdf,
        PdfToolNames.EditPdfPages,
        TabularToolNames.ListTabularData,
        TabularToolNames.QueryTabularData,
    ];

    private readonly ITemplateService _templateService;
    private readonly IPdfWorkspaceStore _workspaceStore;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    /// <param name="workspaceStore">The store of the PDFs the agent keeps for each conversation.</param>
    /// <param name="services">The request services, used to read the conversation's charts.</param>
    public PdfDocumentOrchestrationHandler(
        ITemplateService templateService,
        IPdfWorkspaceStore workspaceStore,
        IServiceProvider services)
    {
        _templateService = templateService;
        _workspaceStore = workspaceStore;
        _services = services;
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
    /// Appends the PDF guidance.
    /// </summary>
    /// <remarks>
    /// The guidance is added before the conversation has any PDF as well. Without it, the first "create a PDF
    /// with that" was answered with generate_file and no file name, which wrote a Markdown file and called it
    /// the PDF; only the agent's description said to prefer it, and that did not hold.
    /// </remarks>
    /// <param name="context">The built context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.OrchestrationContext?.CompletionContext is null)
        {
            return;
        }

        if (context.Resource is AIProfile { Name: PdfAgentProvider.AgentName })
        {
            // The agent has more tools than the orchestrator's scoping threshold, so the orchestrator keeps only
            // the ones whose names and descriptions share the most words with the request. "Make a PDF
            // presentation with charts" kept form, link and annotation tools and dropped preview_pdf and
            // export_pdf: the agent built the document, could not deliver it, and spent its remaining tool
            // rounds on form fields and links until it gave up with "too complex for one pass". The core
            // workflow is kept whatever the request says; scoping still picks among the rest.
            if (!context.OrchestrationContext.DisableTools)
            {
                context.OrchestrationContext.MustIncludeTools.AddRange(_coreAgentToolNames);
            }

            // It is told which charts the reader has already seen, because it receives only the request
            // written for it and not the conversation those charts are in.
            await AppendConversationChartsAsync(context);

            return;
        }

        // A sub-agent's orchestration must not be told to delegate to the PDF agent.
        if (AIInvocationScope.Current?.AgentInvocationDepth > 0)
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

    private async Task AppendConversationChartsAsync(OrchestrationContextBuiltContext context)
    {
        var charts = PdfConversationCharts.Describe(await PdfConversationCharts.GetAsync(_services));

        if (charts is null)
        {
            return;
        }

        context.OrchestrationContext.SystemMessageBuilder.AppendLine();
        context.OrchestrationContext.SystemMessageBuilder.AppendLine();
        context.OrchestrationContext.SystemMessageBuilder.AppendLine("[Charts in the conversation]");
        context.OrchestrationContext.SystemMessageBuilder.Append(charts);
    }
}
