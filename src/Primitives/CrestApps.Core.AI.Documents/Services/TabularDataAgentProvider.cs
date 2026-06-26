using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Contributes the built-in tabular data agent. The agent is always available to the primary
/// model and exposed through A2A, runs its own SQL tools over an in-memory database, and is
/// hidden from the user-facing agent selection list.
/// </summary>
internal sealed class TabularDataAgentProvider : IBuiltInAIAgentProvider
{
    /// <summary>
    /// The technical name of the built-in tabular data agent.
    /// </summary>
    public const string AgentName = "tabular-data-agent";

    private const string AgentItemId = "builtin-tabular-data-agent";

    private const string AgentDescription =
        "Answers questions and performs analysis, calculations, filtering, aggregation, and transformations over uploaded tabular files (CSV, TSV, Excel) by querying an in-memory SQL database instead of reading raw rows. Delegate any request that involves reading, computing over, comparing, or modifying spreadsheet/table data to this agent, passing the user's request in the prompt.";

    private const string SystemPrompt =
        """
        You are the Tabular Data Agent. You answer questions and perform tasks over tabular files
        (CSV, TSV, and Excel) that the user uploaded to the conversation. The data is loaded into an
        in-memory SQLite database so you can work with very large files efficiently.

        How to work:
        1. Call list_tabular_data first to discover the available tables, their columns, and row counts.
        2. Use query_tabular_data to run read-only SQL (SQLite dialect) that directly answers the request.
           Prefer aggregation, filtering, GROUP BY, and small LIMITs. Never try to read every row into your
           answer — push the computation into SQL and return only the result the user needs.
        3. Use execute_tabular_command only when the user asks to modify the data (for example adding or
           removing a column, updating values, or inserting rows). These changes apply to the in-memory copy
           only; the originally uploaded file is always preserved.

        Guidelines:
        - All columns are stored as TEXT. CAST values when you need numeric or date comparisons or math.
        - Quote identifiers with double quotes when they contain spaces or special characters.
        - If a query fails, read the error, correct the SQL, and try again.
        - If there are no tabular files in the conversation, say so plainly.
        - Report results concisely and reference the relevant table and column names.
        """;

    private static readonly IReadOnlyList<AIProfile> _agents = [BuildAgent()];

    /// <summary>
    /// Gets the built-in tabular data agent.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<IReadOnlyList<AIProfile>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_agents);
    }

    private static AIProfile BuildAgent()
    {
        var profile = new AIProfile
        {
            ItemId = AgentItemId,
            Name = AgentName,
            DisplayText = "Tabular Data Agent",
            Type = AIProfileType.Agent,
            Source = "BuiltIn",
            Description = AgentDescription,
        };

        profile.Put(new AgentMetadata
        {
            Availability = AgentAvailability.AlwaysAvailable,
            AllowToolInvocation = true,
            IsBuiltIn = true,
        });

        profile.Put(new FunctionInvocationMetadata
        {
            Names =
            [
                TabularToolNames.ListTabularData,
                TabularToolNames.QueryTabularData,
                TabularToolNames.ExecuteTabularCommand,
            ],
        });

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = SystemPrompt,
            Temperature = 0f,
        });

        return profile;
    }
}
