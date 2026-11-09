using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class GenAIPipelineOrderTests
{
    [Fact]
    public async Task OpenTelemetryUnderFunctionInvocation_TracesEveryRoundTripAndEachToolCall()
    {
        var activities = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CrestAppsTelemetry.AIClientSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue,
        };

        ActivitySource.AddActivityListener(listener);

        var modelId = "model-" + Guid.NewGuid().ToString("N");
        var toolName = "tool_" + Guid.NewGuid().ToString("N");

        // The order NamedAICompletionClient builds: function invocation first, OpenTelemetry added last, so it
        // sits innermost, between the function-invoking client and the provider.
        using var client = new ChatClientBuilder(new ToolCallingChatClient(toolName))
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: CrestAppsTelemetry.AIClientSourceName)
            .Build();

        await client.GetResponseAsync(
            "Call the tool.",
            new ChatOptions
            {
                ModelId = modelId,
                Tools = [AIFunctionFactory.Create(() => "done", toolName)],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, activities.Count(activity => activity.OperationName == $"chat {modelId}"));
        Assert.Single(activities, activity => activity.OperationName == $"execute_tool {toolName}");
    }
}
