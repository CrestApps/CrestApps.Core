using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class AIUsageTelemetryObserverTests
{
    [Fact]
    public async Task UsageRecordedAsync_RecordsTheRequestTokensAndLatencyWithBoundedTags()
    {
        using var collector = new TelemetryCollector();
        var deploymentName = "deployment-" + Guid.NewGuid().ToString("N");

        await new AIUsageTelemetryObserver().UsageRecordedAsync(new AICompletionUsageRecord
        {
            OperationType = AIUsageOperationTypes.Chat,
            ClientName = "OpenAI",
            DeploymentName = deploymentName,
            Purpose = AIUsagePurposes.Conversation,
            IsStreaming = true,
            SessionId = "session-1",
            UserId = "user-1",
            InputTokenCount = 120,
            OutputTokenCount = 30,
            CachedInputTokenCount = 100,
            ResponseLatencyMs = 1500,
        }, TestContext.Current.CancellationToken);

        var request = Assert.Single(collector.GetMeasurements("crestapps.core.ai.usage.requests", AITelemetry.DeploymentTag, deploymentName));

        Assert.Equal(AIUsageOperationTypes.Chat, request.Tags[CrestAppsTelemetry.OperationTag]);
        Assert.Equal("OpenAI", request.Tags[AITelemetry.ClientTag]);
        Assert.Equal(AIUsagePurposes.Conversation, request.Tags[AITelemetry.PurposeTag]);
        Assert.Equal(true, request.Tags[AITelemetry.StreamingTag]);
        Assert.DoesNotContain(request.Tags.Values, value => Equals(value, "session-1") || Equals(value, "user-1"));

        var tokens = collector.GetMeasurements("crestapps.core.ai.usage.tokens", AITelemetry.DeploymentTag, deploymentName)
            .ToDictionary(measurement => (string)measurement.Tags[AITelemetry.TokenTypeTag], measurement => measurement.Value);

        Assert.Equal(120, tokens["input"]);
        Assert.Equal(30, tokens["output"]);
        Assert.Equal(100, tokens["cached"]);
        Assert.False(tokens.ContainsKey("reasoning"));

        var duration = Assert.Single(collector.GetMeasurements("crestapps.core.ai.usage.duration", AITelemetry.DeploymentTag, deploymentName));

        Assert.Equal(1.5, duration.Value);
    }

    [Fact]
    public void AddCoreAIUsageTracking_RegistersTheTelemetryObserverBesideTheStoreObserver()
    {
        var services = new ServiceCollection();

        services.AddCoreAIUsageTracking();

        var observers = services.Where(descriptor => descriptor.ServiceType == typeof(IAICompletionUsageObserver)).ToList();

        Assert.Equal(2, observers.Count);
        Assert.Contains(observers, descriptor => descriptor.ImplementationType == typeof(AIUsageTelemetryObserver));
    }
}
