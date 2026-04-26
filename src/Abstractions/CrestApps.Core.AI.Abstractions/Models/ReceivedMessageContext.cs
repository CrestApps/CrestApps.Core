using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context passed to event handlers when a full AI completion message is received,
/// providing access to the completed <see cref="ChatResponse"/>.
/// </summary>
public sealed class ReceivedMessageContext
{
    public ReceivedMessageContext(ChatResponse completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        Completion = completion;
    }

    /// <summary>
    /// Gets the completed AI response returned by the provider.
    /// </summary>
    public ChatResponse Completion { get; }
}
