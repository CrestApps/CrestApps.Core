using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Mvc.Web.Services;
using Microsoft.AspNetCore.Authorization;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Hubs;

/// <summary>
/// MVC-specific AI chat hub. Inherits all behavior from <see cref="AIChatHubCore"/>.
/// Uses constructor-injected services directly (no ShellScope needed).
/// </summary>
[Authorize]
public sealed class AIChatHub : AIChatHubCore<IAIChatHubClient>
{
    public AIChatHub(
        IServiceProvider services,
        TimeProvider timeProvider,
        ILogger<AIChatHub> logger)
        : base(services, timeProvider, logger)
    {
    }

    protected override void CollectStreamingReferences(
        IServiceProvider services,
        ChatResponseHandlerContext handlerContext,
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds)
    {
        var citationCollector = services.GetRequiredService<SampleCitationReferenceCollector>();

        if (handlerContext.Properties.TryGetValue("OrchestrationContext", out var ctxObj) &&
            ctxObj is OrchestrationContext orchestrationContext)
        {
            citationCollector.CollectPreemptiveReferences(orchestrationContext, references, contentItemIds);
            handlerContext.Properties.Remove("OrchestrationContext");
        }

        citationCollector.CollectToolReferences(references, contentItemIds);
    }

}
