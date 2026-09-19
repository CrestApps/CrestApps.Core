using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Lightweight context for tabular batch processing.
/// Contains only the fields required to call the LLM for batch analysis.
/// </summary>
public sealed class TabularBatchContext
{
    /// <summary>
    /// Gets or sets the AI provider name (e.g., "OpenAI", "AzureOpenAI").
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the AI completion context containing connection, deployment, and model settings.
    /// </summary>
    public AICompletionContext CompletionContext { get; set; }
}
