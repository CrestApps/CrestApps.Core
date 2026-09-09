using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// An <see cref="IRealtimeTurnGrounding"/> whose availability and retrieved content are scripted by the test.
/// </summary>
internal sealed class FakeRealtimeTurnGrounding : IRealtimeTurnGrounding
{
    private readonly bool _available;
    private readonly string _retrieved;

    public FakeRealtimeTurnGrounding(bool available, string retrieved = "RETRIEVED")
    {
        _available = available;
        _retrieved = retrieved;
    }

    /// <summary>
    /// The utterances retrieval ran for, in order.
    /// </summary>
    public List<string> Utterances { get; } = [];

    public bool IsGroundingAvailable(OrchestrationContext context, object resource) => _available;

    public Task<string> RetrieveAsync(OrchestrationContext context, object resource, string utterance, CancellationToken cancellationToken = default)
    {
        Utterances.Add(utterance);

        return Task.FromResult(_retrieved);
    }
}

/// <summary>
/// The grounding a session gets when nothing is attached to retrieve from: the provider keeps answering on its own.
/// </summary>
internal sealed class UngroundedTurnGrounding : IRealtimeTurnGrounding
{
    public static readonly UngroundedTurnGrounding Instance = new();

    public bool IsGroundingAvailable(OrchestrationContext context, object resource) => false;

    public Task<string> RetrieveAsync(OrchestrationContext context, object resource, string utterance, CancellationToken cancellationToken = default)
        => Task.FromResult<string>(null);
}
