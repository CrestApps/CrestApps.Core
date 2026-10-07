using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Labels every session started through a realtime client with a usage category and purpose. A realtime session's
/// usage is labeled when the session starts, so the labels are applied as an <see cref="AIUsageScope"/> around its
/// creation.
/// </summary>
internal sealed class AIUsageLabelingRealtimeClient : DelegatingRealtimeClient
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingRealtimeClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingRealtimeClient(
        IRealtimeClient innerClient,
        string contextType,
        string purpose)
        : base(innerClient)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Creates a session whose usage carries this client's labels.
    /// </summary>
    /// <param name="options">The session options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions options = null,
        CancellationToken cancellationToken = default)
    {
        using var usageScope = AIUsageScope.Begin(_contextType, _purpose);

        return await base.CreateSessionAsync(options, cancellationToken);
    }
}
#pragma warning restore MEAI001 // This client type is for evaluation purposes only and may change in future updates.
