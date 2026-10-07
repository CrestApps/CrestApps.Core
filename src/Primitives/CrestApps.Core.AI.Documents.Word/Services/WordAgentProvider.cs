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
        "Creates, edits, formats, reviews, previews and exports Microsoft Word documents (.docx). Delegate to this agent every request that produces or works on a Word document: writing a report, letter, memo, proposal, contract, resume or manual with a cover page, table of contents, headings, styles, headers and footers, page numbers, tables, images, charts, captions, cross-references and an index, and revising it on follow-ups; showing pages in the chat; working on uploaded .docx files — editing, restyling, adding or removing sections, review comments, tracked changes and editing protection; extracting text, Markdown, tables, links or pictures; searching, comparing versions, and checking accessibility, references and layout. Pass the user's current request with every detail they gave for it. For a follow-up, pass only what this message asks to change: the agent keeps the document between turns, so earlier changes are already made and repeating them would undo the user's later edits. Prefer this agent over generate_file whenever the user wants a Word document that is designed, previewed or changed later.";

    private static readonly string[] _toolNames =
    [
        WordToolNames.CreateWordDocument,
        WordToolNames.AddWordContent,
        WordToolNames.UpdateWordContent,
        WordToolNames.RemoveWordContent,
        WordToolNames.MoveWordContent,
        WordToolNames.GetWordDocument,
        WordToolNames.GetWordDocumentOutline,
        WordToolNames.SearchWordDocument,
        WordToolNames.ExtractWordContent,
        WordToolNames.CompareWordDocuments,
        WordToolNames.CheckWordDocument,
        WordToolNames.AddWordSection,
        WordToolNames.AddWordPageBreak,
        WordToolNames.AddWordToc,
        WordToolNames.AddWordIndex,
        WordToolNames.AddWordCaption,
        WordToolNames.AddWordCrossReference,
        WordToolNames.AddWordBookmark,
        WordToolNames.AddWordHyperlink,
        WordToolNames.UpdateWordTable,
        WordToolNames.FormatWordDocument,
        WordToolNames.FormatWordContent,
        WordToolNames.ManageWordStyles,
        WordToolNames.SetWordPageLayout,
        WordToolNames.SetWordPageBackground,
        WordToolNames.AddWordHeaderFooter,
        WordToolNames.ManageWordComments,
        WordToolNames.ManageWordRevisions,
        WordToolNames.ManageWordProtection,
        WordToolNames.PreviewWord,
        WordToolNames.ExportWord,
        WordToolNames.ImportWord,

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
