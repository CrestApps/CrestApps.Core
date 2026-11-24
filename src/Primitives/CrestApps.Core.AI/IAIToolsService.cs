using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI;

/// <summary>
/// Provides retrieval of registered AI tools by name, supporting tool
/// resolution during completion and orchestration pipelines.
/// </summary>
public interface IAIToolsService
{
    /// <summary>
    /// Retrieves an AI tool by its name, or by one of its aliases (a name it was registered under before).
    /// </summary>
    /// <param name="name">The name of the AI tool, or one of its aliases.</param>
    /// <returns>A task that represents the asynchronous operation, containing the retrieved <see cref="AITool"/>.</returns>
    ValueTask<AITool> GetByNameAsync(string name);
}
