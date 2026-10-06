using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Contributes the system Word agent. The agent is always available to the primary model and exposed through
/// A2A, runs its own Word tools over a per-conversation workspace, and is hidden from the user-facing agent
/// selection list. Its system prompt is sourced from the embedded <c>word-agent</c> AI template so the prompt
/// text stays decoupled from code.
/// </summary>
internal sealed class WordAgentProvider : IAIProfileProvider
{
    /// <summary>
    /// The technical name of the system Word agent.
    /// </summary>
    public const string AgentName = "word-agent";

    /// <summary>
    /// The identifier of the embedded AI template that supplies the agent's system prompt.
    /// </summary>
    public const string SystemPromptTemplateId = "word-agent";

    private const string AgentItemId = "system-word-agent";

    private const string AgentDescription =
        "Creates, edits, formats, reviews, previews and exports Microsoft Word documents (.docx). Delegate to this agent every request that produces or works on a Word document: writing a report, letter, memo, proposal, contract, resume or manual with a cover page, table of contents, headings, styles, headers and footers, page numbers, tables, images, charts, captions and cross-references, and revising it on follow-ups; showing pages in the chat; working on uploaded .docx files — editing, restyling, adding or removing sections, comments and tracked changes, protection; extracting text, tables, images, links, fields or structure; searching, summarizing, comparing versions, answering questions with references, extracting entities or data, classifying, rewriting, improving or translating content; checking accessibility, fonts, links, fields and layout; and converting to or from Word (PDF, HTML, Markdown, text, Excel, PowerPoint, CSV). Pass the user's full request, including every detail they gave. Prefer this agent over generate_file whenever the user wants a Word document that is designed, previewed or changed later.";

    private static readonly string[] _toolNames =
    [
        WordToolNames.CreateWordDocument,
        WordToolNames.AddWordContent,
        WordToolNames.UpdateWordContent,
        WordToolNames.RemoveWordContent,
        WordToolNames.MoveWordContent,
        WordToolNames.GetWordDocument,
        WordToolNames.GetWordDocumentOutline,
        WordToolNames.AddWordSection,
        WordToolNames.AddWordPageBreak,
        WordToolNames.AddWordToc,
        WordToolNames.AddWordIndex,
        WordToolNames.FormatWordDocument,
        WordToolNames.FormatWordContent,
        WordToolNames.ManageWordStyles,
        WordToolNames.SetWordPageLayout,
        WordToolNames.SetWordPageBackground,
        WordToolNames.AddWordHeaderFooter,
        WordToolNames.AddWordPageNumbers,
        WordToolNames.AddWordPageBorders,
        WordToolNames.AddWordHyperlink,
        WordToolNames.AddWordBookmark,
        WordToolNames.AddWordCaption,
        WordToolNames.AddWordCrossReference,
        WordToolNames.AddWordTable,
        WordToolNames.UpdateWordTable,
        WordToolNames.FormatWordTable,
        WordToolNames.MergeWordTableCells,
        WordToolNames.SplitWordTableCell,
        WordToolNames.AddWordImage,
        WordToolNames.UpdateWordImage,
        WordToolNames.AddWordChart,
        WordToolNames.UpdateWordChart,
        WordToolNames.AddWordShape,
        WordToolNames.AddWordSmartArt,
        WordToolNames.ExtractWordText,
        WordToolNames.ExtractWordStructure,
        WordToolNames.ExtractWordTables,
        WordToolNames.ExtractWordImages,
        WordToolNames.ExtractWordLinks,
        WordToolNames.ExtractWordFields,
        WordToolNames.ExtractWordComments,
        WordToolNames.ExtractWordRevisions,
        WordToolNames.SearchWordDocument,
        WordToolNames.GetWordContent,
        WordToolNames.GetWordDocumentInfo,
        WordToolNames.DetectWordLanguage,
        WordToolNames.SummarizeWordDocument,
        WordToolNames.AskWordDocument,
        WordToolNames.CompareWordDocuments,
        WordToolNames.ExtractWordEntities,
        WordToolNames.ExtractWordData,
        WordToolNames.ClassifyWordDocument,
        WordToolNames.GenerateWordOutline,
        WordToolNames.RewriteWordContent,
        WordToolNames.ImproveWordContent,
        WordToolNames.TranslateWordContent,
        WordToolNames.CrossReferenceWordDocuments,
        WordToolNames.AddWordComment,
        WordToolNames.UpdateWordComment,
        WordToolNames.DeleteWordComment,
        WordToolNames.ReplyToWordComment,
        WordToolNames.EnableWordTrackChanges,
        WordToolNames.AcceptWordChanges,
        WordToolNames.RejectWordChanges,
        WordToolNames.GetWordChanges,
        WordToolNames.AddWordTrackedChange,
        WordToolNames.ManageWordProtection,
        WordToolNames.ConvertToWord,
        WordToolNames.ConvertFromWord,
        WordToolNames.ExportWord,
        WordToolNames.ExportWordContent,
        WordToolNames.ImportWord,
        WordToolNames.DuplicateWordDocument,
        WordToolNames.PreviewWord,
        WordToolNames.RenderWordPages,
        WordToolNames.GetWordPageCount,
        WordToolNames.PreviewWordContent,
        WordToolNames.GetWordRenderingInfo,
        WordToolNames.ValidateWordDocument,
        WordToolNames.ValidateWordLayout,
        WordToolNames.CheckWordRendering,
        WordToolNames.CheckWordFonts,
        WordToolNames.CheckWordLinks,
        WordToolNames.CheckWordContentOverflow,
        WordToolNames.CheckWordAccessibility,
        WordToolNames.ValidateWordAccessibility,
        WordToolNames.ValidateWordFields,

        // The agent builds reports from uploaded spreadsheets, so it can look at the tabular workspace to
        // choose the table, the columns and the query a table or chart reads from.
        TabularToolNames.ListTabularData,
        TabularToolNames.QueryTabularData,
    ];

    private readonly ITemplateService _templateService;
    private readonly WordAgentOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordAgentProvider"/> class.
    /// </summary>
    /// <param name="templateService">The template service used to load the agent's system prompt.</param>
    /// <param name="options">The agent options.</param>
    public WordAgentProvider(
        ITemplateService templateService,
        IOptions<WordAgentOptions> options)
    {
        _templateService = templateService;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the names of the tools the agent runs.
    /// </summary>
    public static IReadOnlyList<string> ToolNames => _toolNames;

    /// <summary>
    /// Gets the system Word agent.
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
            DisplayText = "Word Agent",
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
