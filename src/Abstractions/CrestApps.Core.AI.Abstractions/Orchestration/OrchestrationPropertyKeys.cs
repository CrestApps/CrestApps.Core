using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Provides well-known keys for <see cref="OrchestrationContext.Properties"/>.
/// These keys are used by orchestration handlers and the orchestrator to communicate
/// cross-cutting context without direct assembly references.
/// </summary>
public static class OrchestrationPropertyKeys
{
    /// <summary>
    /// Key for a <see cref="System.Collections.Generic.IReadOnlyList{T}"/> of
    /// <see cref="Microsoft.Extensions.AI.AIContent"/> items representing vision image
    /// data to attach to the current user message.
    /// </summary>
    public const string VisionUserContents = "VisionUserContents";
}
