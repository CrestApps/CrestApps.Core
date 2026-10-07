using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Tells the primary model which uploaded files are PowerPoint decks or templates, and that work on them —
/// as opposed to questions about what they say — belongs to the presentation agent.
/// </summary>
/// <remarks>
/// An uploaded deck is also indexed as an ordinary document, so the model can already search and read its
/// text. What it cannot do from there is change it, show it, or restyle it, and without being told it
/// answers such a request by describing the deck instead of delegating.
/// </remarks>
internal sealed class PresentationDocumentOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// The identifier of the template appended to the system message.
    /// </summary>
    public const string TemplateId = "presentation-document-availability";

    private readonly ITemplateService _templateService;
    private readonly PresentationWorkspaceOptions _workspaceOptions;
    private readonly PresentationAgentOptions _agentOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationDocumentOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    /// <param name="workspaceOptions">The workspace options, which say which files are decks.</param>
    /// <param name="agentOptions">The agent options.</param>
    public PresentationDocumentOrchestrationHandler(
        ITemplateService templateService,
        IOptions<PresentationWorkspaceOptions> workspaceOptions,
        IOptions<PresentationAgentOptions> agentOptions)
    {
        _templateService = templateService;
        _workspaceOptions = workspaceOptions.Value;
        _agentOptions = agentOptions.Value;
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
    /// Appends the presentation guidance when the conversation holds uploaded decks or templates.
    /// </summary>
    /// <param name="context">The built context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The agent's own orchestration must not be told to delegate to itself.
        if (!_agentOptions.Enabled || AIInvocationScope.Current?.AgentInvocationDepth > 0 || context.OrchestrationContext?.CompletionContext is null)
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

        var decks = documents?
            .Where(document => document is not null && _workspaceOptions.IsPresentationFile(document.FileName))
            .ToArray();

        if (decks is not { Length: > 0 })
        {
            return;
        }

        var text = await _templateService.RenderAsync(
            TemplateId,
            new Dictionary<string, object>
            {
                ["presentationDocuments"] = decks,
                ["presentationAgentName"] = PresentationAgentProvider.AgentName,
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
