using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class OrchestrationTelemetryTests
{
    [Fact]
    public async Task WithOrchestrationTelemetry_WhenTheTurnCompletes_RecordsDurationAndTimeToFirstToken()
    {
        using var collector = new TelemetryCollector();
        var orchestratorName = "orchestrator-" + Guid.NewGuid().ToString("N");

        var updates = await ReadAllAsync(StreamAsync(["Hello", " world"]).WithOrchestrationTelemetry(orchestratorName, TestContext.Current.CancellationToken));

        Assert.Equal("Hello world", string.Concat(updates.Select(update => update.Text)));

        var duration = Assert.Single(collector.GetMeasurements("crestapps.core.ai.orchestration.duration", AITelemetry.OrchestratorTag, orchestratorName));

        Assert.Equal(CrestAppsTelemetry.OutcomeOk, duration.Tags[CrestAppsTelemetry.OutcomeTag]);
        Assert.Single(collector.GetMeasurements("crestapps.core.ai.orchestration.time_to_first_token", AITelemetry.OrchestratorTag, orchestratorName));
        Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.orchestrate" && Equals(activity.GetTagItem(AITelemetry.OrchestratorTag), orchestratorName));
    }

    [Fact]
    public async Task WithOrchestrationTelemetry_WhenTheOrchestratorFails_RecordsAnErrorAndRethrows()
    {
        using var collector = new TelemetryCollector();
        var orchestratorName = "orchestrator-" + Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAllAsync(FailAfterFirstAsync().WithOrchestrationTelemetry(orchestratorName, TestContext.Current.CancellationToken)));

        var duration = Assert.Single(collector.GetMeasurements("crestapps.core.ai.orchestration.duration", AITelemetry.OrchestratorTag, orchestratorName));

        Assert.Equal(CrestAppsTelemetry.OutcomeError, duration.Tags[CrestAppsTelemetry.OutcomeTag]);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.orchestrate" && Equals(activity.GetTagItem(AITelemetry.OrchestratorTag), orchestratorName));

        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task WithOrchestrationTelemetry_KeepsTheOrchestratorUnderItsSpanAcrossUpdates()
    {
        using var collector = new TelemetryCollector();
        var parents = new List<string>();

        await ReadAllAsync(RecordParentsAsync(parents, TestContext.Current.CancellationToken).WithOrchestrationTelemetry("orchestrator-" + Guid.NewGuid().ToString("N"), TestContext.Current.CancellationToken));

        // Work done after the consumer resumed the stream must still hang under the orchestrate span.
        Assert.Equal(3, parents.Count);
        Assert.All(parents, parent => Assert.Equal("crestapps.orchestrate", parent));
    }

    private static async Task<List<ChatResponseUpdate>> ReadAllAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();

        await foreach (var update in updates)
        {
            // The consumer runs between updates with an activity of its own, as the chat hub does.
            using var consumerActivity = CrestAppsTelemetry.ActivitySource.StartActivity("test.consumer");

            result.Add(update);
        }

        return result;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailAfterFirstAsync()
    {
        await Task.Yield();

        yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello");

        throw new InvalidOperationException("The provider failed.");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> RecordParentsAsync(List<string> parents, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.Yield();

            parents.Add(Activity.Current?.OperationName);

            yield return new ChatResponseUpdate(ChatRole.Assistant, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
