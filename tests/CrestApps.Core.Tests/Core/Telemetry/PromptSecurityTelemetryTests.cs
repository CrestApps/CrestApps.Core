using System.Diagnostics;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class PromptSecurityTelemetryTests
{
    [Fact]
    public void RecordCheck_TagsTheRuleThatBlockedThePrompt()
    {
        using var collector = new TelemetryCollector();
        var rule = "rule-" + Guid.NewGuid().ToString("N");

        PromptSecurityTelemetry.RecordCheck(PromptSecurityResult.Blocked("Blocked.", PromptRiskLevel.High, rule), AITelemetry.InputDirection, Stopwatch.GetTimestamp());

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.ai.security.prompt_checks", AITelemetry.RuleCategoryTag, rule));

        Assert.Equal("blocked", measurement.Tags[AITelemetry.DecisionTag]);
        Assert.Equal(AITelemetry.InputDirection, measurement.Tags[AITelemetry.DirectionTag]);
    }

    [Fact]
    public void RecordCheck_LeavesTheTopicAGuardrailModelNamedOffTheTag()
    {
        using var collector = new TelemetryCollector();
        var category = "rule-" + Guid.NewGuid().ToString("N");

        PromptSecurityTelemetry.RecordCheck(PromptSecurityResult.Blocked("Blocked.", PromptRiskLevel.High, category + ":anything-the-model-said"), AITelemetry.OutputDirection, Stopwatch.GetTimestamp());

        Assert.Single(collector.GetMeasurements("crestapps.core.ai.security.prompt_checks", AITelemetry.RuleCategoryTag, category));
        Assert.DoesNotContain(collector.Measurements, measurement => measurement.Tags.TryGetValue(AITelemetry.RuleCategoryTag, out var value) && value is string text && text.Contains(':'));
    }
}
