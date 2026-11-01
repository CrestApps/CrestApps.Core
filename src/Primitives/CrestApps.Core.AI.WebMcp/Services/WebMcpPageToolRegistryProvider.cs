using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Adds the page tools of the browser tab that sent a chat message to the completion that answers it, when the chat
/// session's profile allows page tools.
/// </summary>
internal sealed class WebMcpPageToolRegistryProvider : IToolRegistryProvider
{
    private readonly IWebMcpPageToolRegistry _registry;
    private readonly IWebMcpPageToolClient _client;
    private readonly IAIProfileManager _profileManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebMcpPageToolRegistryProvider"/> class.
    /// </summary>
    /// <param name="registry">The registry of the page tools each connection offers.</param>
    /// <param name="client">The client that runs page tools in browser tabs.</param>
    /// <param name="profileManager">The AI profile manager.</param>
    public WebMcpPageToolRegistryProvider(
        IWebMcpPageToolRegistry registry,
        IWebMcpPageToolClient client,
        IAIProfileManager profileManager)
    {
        _registry = registry;
        _client = client;
        _profileManager = profileManager;
    }

    /// <summary>
    /// Gets the page tools for a completion: none unless it answers a chat message sent from a connection that
    /// registered page tools, in a session whose profile allows them. Tools that declared the WebMCP
    /// <c>consequentialHint</c> annotation are left out unless the profile also allows those.
    /// </summary>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool registry entries.</returns>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(AICompletionContext context, CancellationToken cancellationToken = default)
    {
        if (context is null ||
            context.DisableTools ||
            !context.AdditionalProperties.TryGetValue(AICompletionContextKeys.ChatConnectionId, out var connectionValue) ||
            connectionValue is not string connectionId ||
            string.IsNullOrEmpty(connectionId) ||
            !context.AdditionalProperties.TryGetValue(AICompletionContextKeys.Session, out var sessionValue) ||
            sessionValue is not AIChatSession session ||
            string.IsNullOrEmpty(session.ProfileId))
        {
            return [];
        }

        var tools = _registry.GetTools(connectionId);

        if (tools.Count == 0)
        {
            return [];
        }

        var profile = await _profileManager.FindByIdAsync(session.ProfileId, cancellationToken);

        if (profile is null ||
            !profile.TryGetSettings<WebMcpProfileSettings>(out var settings) ||
            !settings.AllowPageTools)
        {
            return [];
        }

        return tools
            .Where(tool => !tool.Consequential || settings.AllowConsequentialPageTools)
            .Select(tool => new ToolRegistryEntry
            {
                Id = $"webmcp:{tool.Name}",
                Name = tool.FunctionName,
                Description = tool.Description,
                Source = ToolRegistryEntrySource.Local,
                SourceId = "webmcp",
                CreateAsync = _ => ValueTask.FromResult<AITool>(new WebMcpPageToolFunction(tool, connectionId, _client)),
            })
            .ToList();
    }
}
