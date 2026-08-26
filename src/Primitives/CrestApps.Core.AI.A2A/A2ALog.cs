using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.A2A;

internal static class A2ALog
{
    private static readonly Action<ILogger, string, Exception> _failedToLoadAgentCardForConnection =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1001, nameof(FailedToLoadAgentCardForConnection)),
            "Failed to load agent card for A2A connection '{ConnectionId}'.");

    private static readonly Action<ILogger, string, string, Exception> _failedToFetchAgentCardFromHost =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1002, nameof(FailedToFetchAgentCardFromHost)),
            "Failed to fetch agent card from A2A host '{Endpoint}' for connection '{ConnectionId}'.");

    private static readonly Action<ILogger, string, string, Exception> _failedToCommunicateWithRemoteAgent =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1003, nameof(FailedToCommunicateWithRemoteAgent)),
            "Failed to communicate with remote A2A agent '{AgentName}' at '{Endpoint}'.");

    private static readonly Action<ILogger, Exception> _failedToListLocalAgentProfiles =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1004, nameof(FailedToListLocalAgentProfiles)),
            "Failed to list local agent profiles.");

    private static readonly Action<ILogger, string, Exception> _failedToFetchAgentCardFromConnection =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1005, nameof(FailedToFetchAgentCardFromConnection)),
            "Failed to fetch agent card from A2A connection '{ConnectionId}'.");

    private static readonly Action<ILogger, Exception> _failedToListRemoteA2AAgents =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1006, nameof(FailedToListRemoteA2AAgents)),
            "Failed to list remote A2A agents.");

    private static readonly Action<ILogger, Exception> _failedToSearchForTools =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1007, nameof(FailedToSearchForTools)),
            "Failed to search for tools.");

    private static readonly Action<ILogger, Exception> _failedToSearchLocalAgentProfiles =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1008, nameof(FailedToSearchLocalAgentProfiles)),
            "Failed to search local agent profiles.");

    private static readonly Action<ILogger, Exception> _failedToSearchRemoteA2AAgents =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1009, nameof(FailedToSearchRemoteA2AAgents)),
            "Failed to search remote A2A agents.");

    public static void FailedToLoadAgentCardForConnection(ILogger logger, string connectionId, Exception exception)
    {
        _failedToLoadAgentCardForConnection(logger, connectionId, exception);
    }

    public static void FailedToFetchAgentCardFromHost(ILogger logger, string endpoint, string connectionId, Exception exception)
    {
        _failedToFetchAgentCardFromHost(logger, endpoint, connectionId, exception);
    }

    public static void FailedToCommunicateWithRemoteAgent(ILogger logger, string agentName, string endpoint, Exception exception)
    {
        _failedToCommunicateWithRemoteAgent(logger, agentName, endpoint, exception);
    }

    public static void FailedToListLocalAgentProfiles(ILogger logger, Exception exception)
    {
        _failedToListLocalAgentProfiles(logger, exception);
    }

    public static void FailedToFetchAgentCardFromConnection(ILogger logger, string connectionId, Exception exception)
    {
        _failedToFetchAgentCardFromConnection(logger, connectionId, exception);
    }

    public static void FailedToListRemoteA2AAgents(ILogger logger, Exception exception)
    {
        _failedToListRemoteA2AAgents(logger, exception);
    }

    public static void FailedToSearchForTools(ILogger logger, Exception exception)
    {
        _failedToSearchForTools(logger, exception);
    }

    public static void FailedToSearchLocalAgentProfiles(ILogger logger, Exception exception)
    {
        _failedToSearchLocalAgentProfiles(logger, exception);
    }

    public static void FailedToSearchRemoteA2AAgents(ILogger logger, Exception exception)
    {
        _failedToSearchRemoteA2AAgents(logger, exception);
    }
}
