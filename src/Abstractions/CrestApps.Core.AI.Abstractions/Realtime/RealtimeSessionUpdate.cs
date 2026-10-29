using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// What a running realtime session is changed to by <see cref="IRealtimeConversation.UpdateSessionAsync"/>: the
/// standing instructions and the tools the model may call, both replaced as a whole.
/// </summary>
/// <remarks>
/// The voice is not part of an update. A provider refuses a new voice once the assistant has spoken, and a live
/// conversation always has.
/// </remarks>
public sealed class RealtimeSessionUpdate
{
    /// <summary>
    /// Gets or sets the new standing instructions, or <see langword="null"/> to keep the current ones.
    /// </summary>
    public string Instructions { get; init; }

    /// <summary>
    /// Gets or sets the tools the model may call from now on, or <see langword="null"/> to keep the current ones.
    /// An empty list removes every tool.
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; init; }
}
