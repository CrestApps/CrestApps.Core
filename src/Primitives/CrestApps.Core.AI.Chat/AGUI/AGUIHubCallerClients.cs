using System.Reflection;
using CrestApps.Core.AI.Chat.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CrestApps.Core.AI.Chat.AGUI;

/// <summary>
/// The hub clients of an AG-UI run. The caller's errors (<c>ReceiveError</c> and <c>ReceiveSessionStartRejected</c>)
/// end the run with <c>RUN_ERROR</c> instead of a separate client call; everything else goes to the real clients.
/// Without real clients (a run over HTTP), everything else is dropped.
/// </summary>
/// <typeparam name="TClient">The hub client contract.</typeparam>
internal sealed class AGUIHubCallerClients<TClient> : IHubCallerClients<TClient>
    where TClient : class, IAIChatHubClient
{
    private readonly IHubCallerClients<TClient> _inner;
    private readonly TClient _empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AGUIHubCallerClients{TClient}"/> class.
    /// </summary>
    /// <param name="inner">The real hub clients, or <see langword="null"/> for a run over HTTP.</param>
    /// <param name="turn">The run.</param>
    public AGUIHubCallerClients(IHubCallerClients<TClient> inner, AGUIChatTurn turn)
    {
        _inner = inner;
        _empty = AGUIHubClientProxy.Create<TClient>(inner: null, turn: null);
        Caller = AGUIHubClientProxy.Create(inner?.Caller, turn);
    }

    /// <inheritdoc />
    public TClient Caller { get; }

    /// <inheritdoc />
    public TClient Others => _inner?.Others ?? _empty;

    /// <inheritdoc />
    public TClient All => _inner?.All ?? _empty;

    /// <inheritdoc />
    public TClient OthersInGroup(string groupName) => _inner?.OthersInGroup(groupName) ?? _empty;

    /// <inheritdoc />
    public TClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => _inner?.AllExcept(excludedConnectionIds) ?? _empty;

    /// <inheritdoc />
    public TClient Client(string connectionId) => _inner?.Client(connectionId) ?? _empty;

    /// <inheritdoc />
    public TClient Clients(IReadOnlyList<string> connectionIds) => _inner?.Clients(connectionIds) ?? _empty;

    /// <inheritdoc />
    public TClient Group(string groupName) => _inner?.Group(groupName) ?? _empty;

    /// <inheritdoc />
    public TClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _inner?.GroupExcept(groupName, excludedConnectionIds) ?? _empty;

    /// <inheritdoc />
    public TClient Groups(IReadOnlyList<string> groupNames) => _inner?.Groups(groupNames) ?? _empty;

    /// <inheritdoc />
    public TClient User(string userId) => _inner?.User(userId) ?? _empty;

    /// <inheritdoc />
    public TClient Users(IReadOnlyList<string> userIds) => _inner?.Users(userIds) ?? _empty;
}

/// <summary>
/// The proxy behind the caller of an AG-UI run. <see cref="DispatchProxy"/> requires an unsealed public type.
/// </summary>
public class AGUIHubClientProxy : DispatchProxy
{
    private object _inner;
    private AGUIChatTurn _turn;

    /// <summary>
    /// Creates a client that turns the caller's errors into the run's <c>RUN_ERROR</c> and forwards the rest.
    /// </summary>
    /// <typeparam name="TClient">The hub client contract.</typeparam>
    /// <param name="inner">The real client, or <see langword="null"/> to drop the calls.</param>
    /// <param name="turn">The run, or <see langword="null"/> to treat errors like any other call.</param>
    /// <returns>The client.</returns>
    internal static TClient Create<TClient>(TClient inner, AGUIChatTurn turn)
        where TClient : class
    {
        var proxy = Create<TClient, AGUIHubClientProxy>();
        var dispatch = (AGUIHubClientProxy)(object)proxy;

        dispatch._inner = inner;
        dispatch._turn = turn;

        return proxy;
    }

    /// <summary>
    /// Handles a call of the client.
    /// </summary>
    /// <param name="targetMethod">The method called.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The task of the call.</returns>
    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (_turn is not null && !_turn.Events.IsFinished)
        {
            switch (targetMethod.Name)
            {
                case nameof(IAIChatHubClient.ReceiveError):
                    return _turn.Events.ErrorAsync(args?.FirstOrDefault() as string, code: null).AsTask();

                case nameof(IAIChatHubClient.ReceiveSessionStartRejected):
                    return _turn.Events.ErrorAsync(args?.FirstOrDefault() as string, code: "session_start_rejected").AsTask();
            }
        }

        if (_inner is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return Task.FromException(ex.InnerException);
        }
    }
}
