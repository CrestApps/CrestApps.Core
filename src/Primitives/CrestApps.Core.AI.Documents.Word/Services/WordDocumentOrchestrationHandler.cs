using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Tells the primary model which uploaded files are Word documents and that work on them — as opposed to
/// questions about what they say — belongs to the Word agent.
/// </summary>
/// <remarks>
/// An uploaded Word document is also indexed as an ordinary document, so the model can already search and read
/// it. What it cannot do from there is change it, show it, or read its structure, comments and tracked changes,
/// and without being told it answers such a request by describing the file instead of delegating.
/// </remarks>
internal sealed class WordDocumentOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// The identifier of the template appended to the system message.
    /// </summary>
    public const string TemplateId = "word-document-availability";

    private readonly ITemplateService _templateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordDocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    public WordDocumentOrchestrationHandler(ITemplateService templateService)
    {
        _templateService = templateService;
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
    /// Appends the Word guidance when the conversation holds uploaded Word documents.
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

        if (context.Resource is ChatInteraction interaction)
        {
            documents = interaction.Documents;
        }
        else if (context.Resource is AIProfile &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties is not null &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue("Session", out var sessionObject) &&
            sessionObject is AIChatSession session)
        {
            documents = session.Documents;
        }

        var wordDocuments = documents?
            .Where(document => document is not null && WordPackage.IsWordFile(document.FileName))
            .ToArray();

        if (wordDocuments is not { Length: > 0 })
        {
            return;
        }

        var text = await _templateService.RenderAsync(
            TemplateId,
            new Dictionary<string, object>
            {
                ["wordDocuments"] = wordDocuments,
                ["wordAgentName"] = WordAgentProvider.AgentName,
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
