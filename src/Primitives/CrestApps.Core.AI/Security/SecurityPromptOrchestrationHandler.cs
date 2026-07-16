using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Orchestration handler that prepends a hardened security preamble to the system message
/// to defend against prompt injection, persona manipulation, and data exfiltration attacks.
/// This handler runs during the <see cref="IOrchestrationContextBuilderHandler.BuiltAsync"/> phase
/// to ensure the security preamble is the first content in the final system message.
/// It also wraps user messages with boundary delimiters so the model can distinguish
/// instructions from user-supplied content.
/// </summary>
internal sealed class SecurityPromptOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    private const string SecurityPreambleTemplateId = "security-preamble";
    private const string InputDelimiterTemplateId = "input-delimiter-instructions";
    private const string UserInputBeginDelimiter = "<|user_input_begin|>";
    private const string UserInputEndDelimiter = "<|user_input_end|>";

    private readonly ITemplateService _templateService;
    private readonly IOptions<PromptSecurityOptions> _options;
    private readonly ILogger<SecurityPromptOrchestrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityPromptOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service for rendering the security preamble.</param>
    /// <param name="options">The prompt security options.</param>
    /// <param name="logger">The logger.</param>
    public SecurityPromptOrchestrationHandler(
        ITemplateService templateService,
        IOptions<PromptSecurityOptions> options,
        ILogger<SecurityPromptOrchestrationHandler> logger)
    {
        _templateService = templateService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// No-op during the building phase.
    /// </summary>
    /// <param name="context">The building context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// After the context is fully built, prepends the security preamble to the system message
    /// so it appears as the authoritative first instruction to the model, and wraps the user
    /// message with boundary delimiters to establish clear input boundaries.
    /// Security is only applied to AI Profile-based chats; Chat Interactions are excluded.
    /// </summary>
    /// <param name="context">The built context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        if (context.OrchestrationContext.CompletionContext == null)
        {
            return;
        }

        // Only apply security to AI Profile-based chats.
        // Chat Interactions give the user full control (system prompt, model, MCP)
        // so the security layer is not applicable.
        if (context.Resource is not AIProfile)
        {
            return;
        }

        // Security guards are governed globally by the site-level options.
        var siteOptions = _options.Value;

        // Prepend the security preamble to the system message.
        if (siteOptions.EnableSecurityPreamble)
        {
            var preamble = await _templateService.RenderAsync(SecurityPreambleTemplateId, cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(preamble))
            {
                var existingSystemMessage = context.OrchestrationContext.SystemMessageBuilder.ToString();
                context.OrchestrationContext.SystemMessageBuilder.Clear();
                context.OrchestrationContext.SystemMessageBuilder.Append(preamble);

                if (!string.IsNullOrEmpty(existingSystemMessage))
                {
                    context.OrchestrationContext.SystemMessageBuilder.AppendLine();
                    context.OrchestrationContext.SystemMessageBuilder.AppendLine();
                    context.OrchestrationContext.SystemMessageBuilder.Append(existingSystemMessage);
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Security preamble ({Length} chars) prepended to system message.", preamble.Length);
                }
            }
            else
            {
                _logger.LogDebug("Security preamble template rendered empty; skipping injection.");
            }
        }

        // Wrap user message with delimiters to establish clear input boundaries.
        if (siteOptions.EnableInputDelimiters && !string.IsNullOrEmpty(context.OrchestrationContext.UserMessage))
        {
            // Sanitize user input by removing any injected delimiter tokens.
            var sanitizedMessage = context.OrchestrationContext.UserMessage
                .Replace(UserInputBeginDelimiter, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(UserInputEndDelimiter, string.Empty, StringComparison.OrdinalIgnoreCase);

            context.OrchestrationContext.UserMessage = $"{UserInputBeginDelimiter}\n{sanitizedMessage}\n{UserInputEndDelimiter}";

            // Render the delimiter instruction template so the model knows how to interpret them.
            var delimiterInstruction = await _templateService.RenderAsync(
                InputDelimiterTemplateId,
                new Dictionary<string, object>
                {
                    ["begin_delimiter"] = UserInputBeginDelimiter,
                    ["end_delimiter"] = UserInputEndDelimiter,
                },
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(delimiterInstruction))
            {
                context.OrchestrationContext.SystemMessageBuilder.AppendLine();
                context.OrchestrationContext.SystemMessageBuilder.AppendLine();
                context.OrchestrationContext.SystemMessageBuilder.Append(delimiterInstruction);
            }
        }
    }
}
