using System.Diagnostics;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Records prompt security checks the same way for the text going in and the text coming out.
/// </summary>
internal static class PromptSecurityTelemetry
{
    /// <summary>
    /// Records the duration and decision of one prompt security check.
    /// </summary>
    /// <param name="result">The result of the check.</param>
    /// <param name="direction">Whether the checked text was going in or coming out.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch"/> timestamp taken when the check started.</param>
    public static void RecordCheck(PromptSecurityResult result, string direction, long startTimestamp)
    {
        var directionTag = new KeyValuePair<string, object>(AITelemetry.DirectionTag, direction);

        AITelemetry.PromptEvaluationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, directionTag);
        AITelemetry.PromptChecks.Add(1, directionTag, new KeyValuePair<string, object>(AITelemetry.DecisionTag, GetDecision(result)));
    }

    private static string GetDecision(PromptSecurityResult result)
    {
        if (result.IsBlocked)
        {
            return "blocked";
        }

        return result.RiskLevel == PromptRiskLevel.None
            ? "allowed"
            : "flagged";
    }
}
