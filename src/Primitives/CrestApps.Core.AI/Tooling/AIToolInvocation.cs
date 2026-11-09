using System.Diagnostics;
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
    /// <summary>
    /// Invokes a tool in a <c>crestapps.tool.invoke</c> span and records its duration, source and outcome on
    /// <see cref="AITelemetry.ToolDuration"/>.
    /// </summary>
    /// <param name="function">The tool to invoke.</param>
    /// <param name="arguments">The arguments the model passed to it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool's result, unchanged.</returns>
    public static async ValueTask<object> InvokeAsync(AIFunction function, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);

        var toolSource = GetToolSource(function.Name);

        using var activity = CrestAppsTelemetry.ActivitySource.StartActivity("crestapps.tool.invoke");
        activity?.SetTag(AITelemetry.ToolNameTag, function.Name);
        activity?.SetTag(AITelemetry.ToolSourceTag, toolSource);

        var startTimestamp = Stopwatch.GetTimestamp();
        var outcome = CrestAppsTelemetry.OutcomeOk;

        try
        {
            return await function.InvokeAsync(arguments, cancellationToken);
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
