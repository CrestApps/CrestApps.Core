using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Contributes the system presentation agent. The agent is always available to the primary model and
/// exposed through A2A, runs its own PowerPoint tools over a per-conversation workspace of decks, and is
/// hidden from the user-facing agent selection list. Its system prompt is sourced from the embedded
/// <c>presentation-agent</c> AI template so the prompt text stays decoupled from code.
/// </summary>
internal sealed class PresentationAgentProvider : IAIProfileProvider
{
    /// <summary>
    /// The technical name of the system presentation agent.
    /// </summary>
    public const string AgentName = "presentation-agent";

    /// <summary>
    /// The identifier of the embedded AI template that supplies the agent's system prompt.
    /// </summary>
    public const string SystemPromptTemplateId = "presentation-agent";

    private const string AgentItemId = "system-presentation-agent";

    private const string AgentDescription =
        "Creates, edits, designs and previews PowerPoint presentations (.pptx). Delegate to this agent every request that produces or works on a slide deck: making a deck on a topic or from a document or data (\"make me a 6-slide deck about X\"); adding, removing, reordering, duplicating or rewriting slides; changing layouts, themes, colours, fonts, backgrounds, masters and branding (logo, footer, slide numbers); inserting and editing text, bullets, shapes, icons, pictures (uploaded or generated), tables, charts, diagrams (process, timeline, org chart, cycle, pyramid, funnel, matrix) and agenda slides; charts and tables from uploaded spreadsheet data; speaker notes, scripts and timing; reviewing a deck for layout, consistency, accessibility and readability; summarising, searching, comparing or translating a deck; showing slides in the chat; and exporting the deck as .pptx or PDF, or its outline and notes as a document. Pass the user's full request with every detail they gave. Prefer this agent over generate_file for anything that is a presentation.";

    private static readonly string[] _toolNames =
    [
        PresentationToolNames.GetPresentationOutline,
        PresentationToolNames.GetSlideContent,
        PresentationToolNames.GetPresentationTheme,
        PresentationToolNames.SearchPresentation,
        PresentationToolNames.ExtractPresentationContent,
        PresentationToolNames.CreatePresentation,
        PresentationToolNames.AddSlide,
        PresentationToolNames.DuplicateSlide,
        PresentationToolNames.DeleteSlide,
        PresentationToolNames.MoveSlide,
        PresentationToolNames.UpdateSlide,
        PresentationToolNames.InsertSlideElement,
        PresentationToolNames.UpdateSlideElement,
        PresentationToolNames.UpdateSlideText,
        PresentationToolNames.CopySlideElements,
        PresentationToolNames.DeleteSlideElement,
        PresentationToolNames.GroupSlideElements,
        PresentationToolNames.ArrangeSlideElements,
        PresentationToolNames.UpdateSlideTable,
        PresentationToolNames.UpdateSlideChart,
        PresentationToolNames.LinkSlideData,
        PresentationToolNames.RefreshSlideData,
        PresentationToolNames.FormatPresentation,
        PresentationToolNames.ApplySlideLayout,
        PresentationToolNames.ApplyPresentationTemplate,
        PresentationToolNames.SetSlideBackground,
        PresentationToolNames.UpdatePresentationTheme,
        PresentationToolNames.UpdateSlideMaster,
        PresentationToolNames.ApplyPresentationBranding,
        PresentationToolNames.SetPresentationFooter,
        PresentationToolNames.UpdatePresentationSections,
        PresentationToolNames.AddPresentationToc,
        PresentationToolNames.GenerateSlideImage,
        PresentationToolNames.GenerateSlideDiagram,
        PresentationToolNames.AnalyzePresentation,
        PresentationToolNames.CheckPresentation,
        PresentationToolNames.ComparePresentations,
        PresentationToolNames.PreviewPresentation,
        PresentationToolNames.ExportPresentation,
        PresentationToolNames.ExportPresentationContent,
        PresentationToolNames.UndoPresentationChange,

        // Decks are often built from uploaded spreadsheets, so the agent can look at the tabular workspace
        // to choose the table, the columns and the query a slide's table or chart reads from.
        TabularToolNames.ListTabularData,
        TabularToolNames.QueryTabularData,
    ];

    private readonly ITemplateService _templateService;
    private readonly PresentationAgentOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationAgentProvider"/> class.
    /// </summary>
    /// <param name="templateService">The template service used to load the agent's system prompt.</param>
    /// <param name="options">The agent options.</param>
    public PresentationAgentProvider(ITemplateService templateService, IOptions<PresentationAgentOptions> options)
    {
        _templateService = templateService;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the names of the tools the agent runs.
    /// </summary>
    public static IReadOnlyList<string> ToolNames => _toolNames;

    /// <summary>
    /// Gets the system presentation agent.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyList<AIProfile>> GetProfilesAsync(
        AIProfileType type,
        CancellationToken cancellationToken = default)
    {
        if (type != AIProfileType.Agent || !_options.Enabled)
        {
            return [];
        }

        var systemPrompt = await _templateService.RenderAsync(SystemPromptTemplateId, cancellationToken: cancellationToken);

        return [BuildAgent(systemPrompt)];
    }

    private static AIProfile BuildAgent(string systemPrompt)
    {
        var profile = new AIProfile
        {
            ItemId = AgentItemId,
            Name = AgentName,
            DisplayText = "Presentation Agent",
            Type = AIProfileType.Agent,
            Source = "System",
            Description = AgentDescription,
        };

        profile.Put(new AgentMetadata
        {
            Availability = AgentAvailability.AlwaysAvailable,
            AllowToolInvocation = true,
            IsSystem = true,
        });

        profile.Put(new FunctionInvocationMetadata
        {
            Names = [.. _toolNames],
        });

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = systemPrompt,
            Temperature = null,
        });

        return profile;
    }
}
