using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Contributes the system tabular data agent. The agent is always available to the primary
/// model and exposed through A2A, runs its own SQL tools over an in-memory database, and is
/// hidden from the user-facing agent selection list. Its system prompt is sourced from the
/// embedded <c>tabular-data-agent</c> AI template so the prompt text stays decoupled from code.
/// </summary>
internal sealed class TabularDataAgentProvider : IAIProfileProvider
{
    /// <summary>
    /// The technical name of the system tabular data agent.
    /// </summary>
    public const string AgentName = "tabular-data-agent";

    /// <summary>
    /// The identifier of the embedded AI template that supplies the agent's system prompt.
    /// </summary>
    public const string SystemPromptTemplateId = "tabular-data-agent";

    private const string AgentItemId = "system-tabular-data-agent";

    private const string AgentDescription =
        "Answers questions and performs analysis, calculations, filtering, aggregation, and transformations over uploaded tabular files (such as CSV and Excel) by querying an in-memory SQL database instead of reading raw rows. Delegate any request that involves reading, computing over, comparing, or modifying spreadsheet/table data to this agent, passing the user's request in the prompt.";

    private readonly ITemplateService _templateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularDataAgentProvider"/> class.
    /// </summary>
    /// <param name="templateService">The template service used to load the agent's system prompt.</param>
    public TabularDataAgentProvider(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    /// <summary>
    /// Gets the system tabular data agent.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyList<AIProfile>> GetProfilesAsync(
        AIProfileType type,
        CancellationToken cancellationToken = default)
    {
        if (type != AIProfileType.Agent)
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
            DisplayText = "Tabular Data Agent",
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
            Names =
            [
                TabularToolNames.ListTabularData,
                TabularToolNames.QueryTabularData,
                TabularToolNames.ExecuteTabularCommand,
                TabularToolNames.ExportTabularData,
            ],
        });

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = systemPrompt,
            Temperature = 0f,
        });

        return profile;
    }
}
