using CrestApps.Core.AI;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class ToolInvocationTelemetryTests
{
    [Fact]
    public async Task InvokingATool_RecordsItsDurationAndSourceAndStartsAToolSpan()
    {
        using var collector = new TelemetryCollector();
        var toolName = "tool_" + Guid.NewGuid().ToString("N");

        await InvokeToolAsync(AIFunctionFactory.Create(() => "done", toolName), ToolRegistryEntrySource.McpServer);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.tool.invoke" && Equals(activity.GetTagItem(AITelemetry.ToolNameTag), toolName));

        Assert.Equal("mcp", span.GetTagItem(AITelemetry.ToolSourceTag));

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.ai.tool.duration", AITelemetry.ToolSourceTag, "mcp"), item => Equals(item.Tags[CrestAppsTelemetry.OutcomeTag], CrestAppsTelemetry.OutcomeOk));

        Assert.True(measurement.Value >= 0);
    }

    [Fact]
    public async Task InvokingAToolThatThrows_RecordsAnErrorOutcomeAndMarksTheSpan()
    {
        using var collector = new TelemetryCollector();
        var toolName = "tool_" + Guid.NewGuid().ToString("N");

        await InvokeToolAsync(AIFunctionFactory.Create(string () => throw new InvalidOperationException("boom"), toolName), ToolRegistryEntrySource.A2AAgent);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.tool.invoke" && Equals(activity.GetTagItem(AITelemetry.ToolNameTag), toolName));

        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, span.Status);
        Assert.Contains(collector.GetMeasurements("crestapps.core.ai.tool.duration", AITelemetry.ToolSourceTag, "a2a"), item => Equals(item.Tags[CrestAppsTelemetry.OutcomeTag], CrestAppsTelemetry.OutcomeError));
    }

    [Fact]
    public async Task InvokingAToolTheOrchestratorDidNotScope_RecordsAnUnknownSource()
    {
        using var collector = new TelemetryCollector();
        var toolName = "tool_" + Guid.NewGuid().ToString("N");

        await InvokeToolAsync(AIFunctionFactory.Create(() => "done", toolName), source: null);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.tool.invoke" && Equals(activity.GetTagItem(AITelemetry.ToolNameTag), toolName));

        Assert.Equal("unknown", span.GetTagItem(AITelemetry.ToolSourceTag));
    }

    private static async Task InvokeToolAsync(AIFunction tool, ToolRegistryEntrySource? source)
    {
        using var invocationScope = AIInvocationScope.Begin();

        if (source is not null)
        {
            var completionContext = new AICompletionContext();
            completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = new List<ToolRegistryEntry>
            {
                new()
                {
                    Name = tool.Name,
                    Source = source.Value,
                },
            };

            AIInvocationScope.Current.CompletionContext = completionContext;
        }

        using var client = new FunctionInvokingChatClient(new ToolCallingChatClient(tool.Name)).UseTextToolResults();

        await client.GetResponseAsync("Call the tool.", new ChatOptions { Tools = [tool] });
    }
}
