using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Invokes the tools the framework hands to a model, so every tool call is traced and timed the same way whichever
/// client runs the tool-calling loop.
/// </summary>
public static class AIToolInvocation
{
    // Set for the duration of each invocation; a tool that turns its failure into a result marks it here.
    private static readonly AsyncLocal<StrongBox<bool>> _failure = new();

    /// <summary>
    /// Invokes a tool in a <c>crestapps.tool.invoke</c> span and records its duration, source and outcome on
    /// <see cref="AITelemetry.ToolDuration"/>.
    /// </summary>
    /// <param name="function">The tool to invoke.</param>
    /// <param name="arguments">The arguments the model passed to it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool's result, unchanged.</returns>
    public static ValueTask<object> InvokeAsync(AIFunction function, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);

        return InvokeAsync(function, arguments, GetToolSource(function.Name), cancellationToken);
    }

    /// <summary>
    /// Invokes a tool that was not handed to a model by the orchestrator, such as one an MCP or WebMCP client
    /// called, in a <c>crestapps.tool.invoke</c> span, and records it on <see cref="AITelemetry.ToolDuration"/> under
    /// the given source.
    /// </summary>
    /// <param name="function">The tool to invoke.</param>
    /// <param name="arguments">The arguments the caller passed to it.</param>
    /// <param name="toolSource">The bounded <see cref="AITelemetry.ToolSourceTag"/> value, such as <c>mcp_server</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool's result, unchanged.</returns>
    public static async ValueTask<object> InvokeAsync(AIFunction function, AIFunctionArguments arguments, string toolSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentException.ThrowIfNullOrEmpty(toolSource);

        using var activity = CrestAppsTelemetry.ActivitySource.StartActivity("crestapps.tool.invoke");
        activity?.SetTag(AITelemetry.ToolNameTag, function.Name);
        activity?.SetTag(AITelemetry.ToolSourceTag, toolSource);

        var startTimestamp = Stopwatch.GetTimestamp();
        var outcome = CrestAppsTelemetry.OutcomeOk;
        var failure = new StrongBox<bool>();

        _failure.Value = failure;

        try
        {
            var result = await function.InvokeAsync(arguments, cancellationToken);

            if (failure.Value)
            {
                outcome = CrestAppsTelemetry.OutcomeError;
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            outcome = CrestAppsTelemetry.GetOutcome(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

            throw;
        }
        finally
        {
            AITelemetry.ToolDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object>(AITelemetry.ToolSourceTag, toolSource),
                new KeyValuePair<string, object>(CrestAppsTelemetry.OutcomeTag, outcome));
        }
    }

    /// <summary>
    /// Marks the tool call in progress as failed. A tool that hands its failure back to the model as a result, so
    /// the model can recover, calls this so the call is still counted and traced as a failure.
    /// </summary>
    public static void ReportFailure()
    {
        if (_failure.Value is { } failure)
        {
            failure.Value = true;
        }
    }

    /// <summary>
    /// Gets where a tool comes from, read from the tool registry entries scoped for the current request.
    /// </summary>
    /// <param name="toolName">The name of the invoked tool.</param>
    /// <returns>The bounded tool source, or <c>unknown</c> when the tool was not handed over by the orchestrator.</returns>
    private static string GetToolSource(string toolName)
    {
        if (AIInvocationScope.Current?.CompletionContext?.AdditionalProperties.TryGetValue(FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey, out var value) == true &&
            value is IReadOnlyList<ToolRegistryEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Name, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Source switch
                    {
                        ToolRegistryEntrySource.McpServer => "mcp",
                        ToolRegistryEntrySource.System => "system",
                        ToolRegistryEntrySource.Agent => "agent",
                        ToolRegistryEntrySource.A2AAgent => "a2a",
                        _ => "local",
                    };
                }
            }
        }

        return "unknown";
    }
}
