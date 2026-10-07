using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tools.Presentations;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Extension methods for registering the system presentation agent.
/// </summary>
public static class PresentationServiceCollectionExtensions
{
    private const string Category = "Presentations";

    /// <summary>
    /// Adds the system presentation agent: its tools, its per-conversation workspace of decks, the guidance
    /// that steers PowerPoint work to it, and the handlers that clean the workspace up.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The agent reads and edits decks through <see cref="IPresentationEngine"/>, which a PowerPoint package
    /// such as <c>CrestApps.Core.AI.Documents.OpenXml</c> registers along with this; without an engine the
    /// tools report that PowerPoint support is not enabled. Registering it more than once adds nothing.
    /// </remarks>
    public static IServiceCollection AddCoreAIPresentationAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(PresentationAgentMarker)))
        {
            return services;
        }

        services.AddSingleton<PresentationAgentMarker>();
        services.AddOptions<PresentationWorkspaceOptions>();
        services.AddOptions<PresentationPreviewOptions>();
        services.AddOptions<PresentationAgentOptions>();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProfileProvider, PresentationAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, PresentationDocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationWorkspaceCleanupHandler, PresentationWorkspaceCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, PresentationWorkspaceHistoryClearedHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatDocumentEventHandler, PresentationWorkspaceDocumentEventHandler>());

        services.AddCoreAITool<GetPresentationOutlineTool>(GetPresentationOutlineTool.TheName)
            .WithTitle("Get Presentation Outline")
            .WithDescription("Lists the slides of a presentation with their layouts, titles and notes, and the presentations in the conversation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<GetSlideContentTool>(GetSlideContentTool.TheName)
            .WithTitle("Get Slide Content")
            .WithDescription("Returns every element of a slide with its id, role, position, text and style.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<GetPresentationThemeTool>(GetPresentationThemeTool.TheName)
            .WithTitle("Get Presentation Theme")
            .WithDescription("Returns the colours, fonts, masters and layouts of a presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<SearchPresentationTool>(SearchPresentationTool.TheName)
            .WithTitle("Search Presentation")
            .WithDescription("Finds text across the slides, tables, charts, alternative text and notes of a presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ExtractPresentationContentTool>(ExtractPresentationContentTool.TheName)
            .WithTitle("Extract Presentation Content")
            .WithDescription("Extracts the text, notes, tables, charts, pictures, links or structure of a presentation as data.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<CreatePresentationTool>(CreatePresentationTool.TheName)
            .WithTitle("Create Presentation")
            .WithDescription("Creates a presentation from a theme or an uploaded template, optionally with all of its slides.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<AddSlideTool>(AddSlideTool.TheName)
            .WithTitle("Add Slide")
            .WithDescription("Adds slides with a layout, title, body, notes and elements.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<DuplicateSlideTool>(DuplicateSlideTool.TheName)
            .WithTitle("Duplicate Slide")
            .WithDescription("Duplicates a slide with everything on it.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<DeleteSlideTool>(DeleteSlideTool.TheName)
            .WithTitle("Delete Slide")
            .WithDescription("Deletes slides.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<MoveSlideTool>(MoveSlideTool.TheName)
            .WithTitle("Move Slide")
            .WithDescription("Reorders slides.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideTool>(UpdateSlideTool.TheName)
            .WithTitle("Update Slide")
            .WithDescription("Changes the title, body, notes, layout, background, transition or visibility of slides.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<InsertSlideElementTool>(InsertSlideElementTool.TheName)
            .WithTitle("Insert Slide Element")
            .WithDescription("Places text, shapes, lines, tables, charts, pictures, icons, diagrams, media links and groups on a slide.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideElementTool>(UpdateSlideElementTool.TheName)
            .WithTitle("Update Slide Element")
            .WithDescription("Moves, resizes, restyles, re-links, replaces or crops an element.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideTextTool>(UpdateSlideTextTool.TheName)
            .WithTitle("Update Slide Text")
            .WithDescription("Rewrites paragraphs, finds and replaces text, and writes speaker notes while keeping formatting.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<CopySlideElementsTool>(CopySlideElementsTool.TheName)
            .WithTitle("Copy Slide Elements")
            .WithDescription("Copies or moves elements to the same or another slide.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<DeleteSlideElementTool>(DeleteSlideElementTool.TheName)
            .WithTitle("Delete Slide Element")
            .WithDescription("Deletes elements from a slide.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<GroupSlideElementsTool>(GroupSlideElementsTool.TheName)
            .WithTitle("Group Slide Elements")
            .WithDescription("Groups or ungroups elements.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ArrangeSlideElementsTool>(ArrangeSlideElementsTool.TheName)
            .WithTitle("Arrange Slide Elements")
            .WithDescription("Aligns, distributes, sizes, layers and lays out elements.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideTableTool>(UpdateSlideTableTool.TheName)
            .WithTitle("Update Slide Table")
            .WithDescription("Edits the cells, rows, columns and style of a table, or fills it from tabular data.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideChartTool>(UpdateSlideChartTool.TheName)
            .WithTitle("Update Slide Chart")
            .WithDescription("Changes the type, data, titles and style of a chart, or fills it from tabular data.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<LinkSlideDataTool>(LinkSlideDataTool.TheName)
            .WithTitle("Link Slide Data")
            .WithDescription("Ties a table or chart to a query over the tabular data of the conversation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<RefreshSlideDataTool>(RefreshSlideDataTool.TheName)
            .WithTitle("Refresh Slide Data")
            .WithDescription("Updates linked tables and charts from their queries.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<FormatPresentationTool>(FormatPresentationTool.TheName)
            .WithTitle("Format Presentation")
            .WithDescription("Restyles text, shapes, tables, charts and backgrounds consistently and remembers the house style.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ApplySlideLayoutTool>(ApplySlideLayoutTool.TheName)
            .WithTitle("Apply Slide Layout")
            .WithDescription("Switches slides to another layout, keeping their content.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ApplyPresentationTemplateTool>(ApplyPresentationTemplateTool.TheName)
            .WithTitle("Apply Presentation Template")
            .WithDescription("Gives a presentation the design of an uploaded template or another presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<SetSlideBackgroundTool>(SetSlideBackgroundTool.TheName)
            .WithTitle("Set Slide Background")
            .WithDescription("Sets slide backgrounds to a colour, gradient or picture.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdatePresentationThemeTool>(UpdatePresentationThemeTool.TheName)
            .WithTitle("Update Presentation Theme")
            .WithDescription("Changes the theme colours and fonts, or switches to a built-in theme.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdateSlideMasterTool>(UpdateSlideMasterTool.TheName)
            .WithTitle("Update Slide Master")
            .WithDescription("Changes the slide master or a layout: background, text styles and placeholder positions.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ApplyPresentationBrandingTool>(ApplyPresentationBrandingTool.TheName)
            .WithTitle("Apply Presentation Branding")
            .WithDescription("Applies brand colours, fonts, a logo and footer text in one step.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<SetPresentationFooterTool>(SetPresentationFooterTool.TheName)
            .WithTitle("Set Presentation Footer")
            .WithDescription("Sets footer text, slide numbers and the date.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UpdatePresentationSectionsTool>(UpdatePresentationSectionsTool.TheName)
            .WithTitle("Update Presentation Sections")
            .WithDescription("Organises a presentation into named sections.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<AddPresentationTocTool>(AddPresentationTocTool.TheName)
            .WithTitle("Add Presentation TOC")
            .WithDescription("Adds or rewrites an agenda slide whose entries link to their slides.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<GenerateSlideImageTool>(GenerateSlideImageTool.TheName)
            .WithTitle("Generate Slide Image")
            .WithDescription("Generates a picture with the image model and places it on a slide or behind it.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<GenerateSlideDiagramTool>(GenerateSlideDiagramTool.TheName)
            .WithTitle("Generate Slide Diagram")
            .WithDescription("Draws a process, timeline, hierarchy, cycle, pyramid, funnel, matrix, Venn or card diagram of editable shapes.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<AnalyzePresentationTool>(AnalyzePresentationTool.TheName)
            .WithTitle("Analyze Presentation")
            .WithDescription("Measures the storyline, density, timing, visuals and design of a presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<CheckPresentationTool>(CheckPresentationTool.TheName)
            .WithTitle("Check Presentation")
            .WithDescription("Reviews a presentation for layout, consistency, accessibility, readability, link, media, file and rendering problems.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ComparePresentationsTool>(ComparePresentationsTool.TheName)
            .WithTitle("Compare Presentations")
            .WithDescription("Compares two presentations, or a presentation with an earlier version or its original upload.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<PreviewPresentationTool>(PreviewPresentationTool.TheName)
            .WithTitle("Preview Presentation")
            .WithDescription("Shows slides as pictures in the conversation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ExportPresentationTool>(ExportPresentationTool.TheName)
            .WithTitle("Export Presentation")
            .WithDescription("Creates a downloadable PowerPoint or PDF file of a presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<ExportPresentationContentTool>(ExportPresentationContentTool.TheName)
            .WithTitle("Export Presentation Content")
            .WithDescription("Exports the outline, notes, presenter script, handout or text of a presentation.")
            .WithCategory(Category)
            .Hidden();

        services.AddCoreAITool<UndoPresentationChangeTool>(UndoPresentationChangeTool.TheName)
            .WithTitle("Undo Presentation Change")
            .WithDescription("Undoes recent changes to a presentation.")
            .WithCategory(Category)
            .Hidden();

        return services;
    }

    /// <summary>
    /// Marks the agent as registered, so a second call adds nothing.
    /// </summary>
    private sealed class PresentationAgentMarker
    {
    }
}
