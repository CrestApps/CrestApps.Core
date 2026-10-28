using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Adds the sections of the profile's speaking styles to the end of its system message, after the persona and
/// instructions they refine. Each style is a prompt template, rendered in the order the profile lists them.
/// </summary>
public sealed class AISpeakingStyleCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    private readonly ITemplateService _templateService;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AISpeakingStyleCompletionContextBuilderHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service the style sections are read from.</param>
    /// <param name="logger">The logger.</param>
    public AISpeakingStyleCompletionContextBuilderHandler(
        ITemplateService templateService,
        ILogger<AISpeakingStyleCompletionContextBuilderHandler> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    /// <summary>
    /// Does nothing; the sections are added once the system message is built.
    /// </summary>
    /// <param name="context">The building context.</param>
    /// <returns>A completed task.</returns>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Appends the rendered sections of the profile's speaking styles to the system message.
    /// </summary>
    /// <param name="context">The built context.</param>
    /// <returns>A task that completes when the sections are added.</returns>
    public async Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Resource is not AIProfile profile ||
            !profile.TryGetSettings<AIProfileSpeakingStyleSettings>(out var settings))
        {
            return;
        }

        var sections = new List<string>();

        foreach (var style in AISpeakingStyles.Normalize(settings.Styles))
        {
            try
            {
                var section = await _templateService.RenderAsync(style);

                if (!string.IsNullOrWhiteSpace(section))
                {
                    sections.Add(section.Trim());
                }
            }
            catch (Exception ex)
            {
                // A missing or broken template leaves its style out rather than failing the conversation.
                _logger.LogWarning(ex, "The speaking style '{Style}' could not be added to the system message.", style);
            }
        }

        if (sections.Count == 0)
        {
            return;
        }

        var separator = Environment.NewLine + Environment.NewLine;

        context.Context.SystemMessage = string.IsNullOrWhiteSpace(context.Context.SystemMessage)
            ? string.Join(separator, sections)
            : context.Context.SystemMessage.TrimEnd() + separator + string.Join(separator, sections);
    }
}
