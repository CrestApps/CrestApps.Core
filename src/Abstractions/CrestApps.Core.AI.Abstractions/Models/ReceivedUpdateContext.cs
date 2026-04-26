using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context passed to event handlers when a streamed AI completion update is received,
/// providing access to the partial <see cref="ChatResponseUpdate"/>.
/// </summary>
public sealed class ReceivedUpdateContext
{
    public ReceivedUpdateContext(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        Update = update;
    }

    /// <summary>
    /// Gets the streamed update chunk received from the AI provider.
    /// </summary>
    public ChatResponseUpdate Update { get; }
}
