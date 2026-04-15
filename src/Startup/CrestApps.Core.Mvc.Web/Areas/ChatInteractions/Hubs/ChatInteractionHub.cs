using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Models;
using CrestApps.Core.Mvc.Web.Services;
using Microsoft.AspNetCore.Authorization;

namespace CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Hubs;

[Authorize]
public sealed class ChatInteractionHub : ChatInteractionHubBase
{
    private readonly MvcCitationReferenceCollector _citationCollector;
    private readonly SiteSettingsStore _siteSettings;

    public ChatInteractionHub(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        MvcCitationReferenceCollector citationCollector,
        SiteSettingsStore siteSettings,
        ILogger<ChatInteractionHub> logger)
        : base(serviceProvider, timeProvider, logger)
    {
        _citationCollector = citationCollector;
        _siteSettings = siteSettings;
    }

    protected override void CollectStreamingReferences(
        IServiceProvider services,
        ChatResponseHandlerContext handlerContext,
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds)
    {
        _citationCollector.CollectToolReferences(references, contentItemIds);
    }

    protected override Task<ChatMode> GetChatModeAsync(IServiceProvider services)
    {
        var settings = _siteSettings.Get<ChatInteractionSettings>();
        return Task.FromResult(settings.ChatMode);
    }

    protected override Task<bool> IsTextToSpeechPlaybackEnabledAsync(IServiceProvider services)
    {
        var settings = _siteSettings.Get<ChatInteractionSettings>();
        return Task.FromResult(settings.EnableTextToSpeechPlayback);
    }

    protected override Task<DefaultAIDeploymentSettings> GetDeploymentSettingsAsync(IServiceProvider services)
    {
        return Task.FromResult(_siteSettings.Get<DefaultAIDeploymentSettings>());
    }
}
