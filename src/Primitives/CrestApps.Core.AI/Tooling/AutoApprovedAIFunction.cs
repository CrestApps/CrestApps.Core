using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A tool that requires an approval and runs without asking, because the request's approval policy allows it. Each
/// call is logged at Information level and counted in <see cref="AITelemetry.ToolAutoApprovals"/>, so a call that
/// nobody approved is never silent.
/// </summary>
internal sealed class AutoApprovedAIFunction : DelegatingAIFunction
{
    /// <summary>
    /// The reason recorded for a call the profile's approval policy let run.
    /// </summary>
    public const string PolicyReason = "policy";

    private readonly AIToolKind _kind;
    private readonly ILogger _logger;

    public AutoApprovedAIFunction(AIFunction innerFunction, AIToolKind kind, ILogger logger)
        : base(innerFunction)
    {
        _kind = kind;
        _logger = logger;
    }

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var invocation = AIInvocationScope.Current;
        var inAgent = invocation?.AgentInvocationDepth > 0;

        AITelemetry.ToolAutoApprovals.Add(
            1,
            new KeyValuePair<string, object>(AITelemetry.ReasonTag, PolicyReason),
            new KeyValuePair<string, object>(AITelemetry.ToolKindTag, _kind.ToString()),
            new KeyValuePair<string, object>(AITelemetry.AgentTag, inAgent ? "true" : "false"));

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Tool '{ToolName}' ({ToolKind}) requires an approval and ran without asking, because the profile's tool approval policy allows it. InAgent={InAgent}, SessionId={SessionId}.",
                Name,
                _kind,
                inAgent,
                invocation?.ChatSession?.SessionId);
        }

        return base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
