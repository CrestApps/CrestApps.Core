using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Traces an orchestrator's streamed reply, whichever orchestrator produced it.
/// </summary>
internal static class OrchestrationTelemetryExtensions
{
    /// <summary>
    /// Wraps an orchestrator's updates in a <c>crestapps.orchestrate</c> span and records the turn's duration and
    /// time to first token.
    /// </summary>
    /// <param name="updates">The orchestrator's streamed updates.</param>
    /// <param name="orchestratorName">The name of the orchestrator, recorded as a bounded tag.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The same updates, unchanged.</returns>
    public static async IAsyncEnumerable<ChatResponseUpdate> WithOrchestrationTelemetry(
        this IAsyncEnumerable<ChatResponseUpdate> updates,
        string orchestratorName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = CrestAppsTelemetry.ActivitySource.StartActivity("crestapps.orchestrate");
        activity?.SetTag(AITelemetry.OrchestratorTag, orchestratorName);

        var orchestratorTag = new KeyValuePair<string, object>(AITelemetry.OrchestratorTag, orchestratorName);
        var startTimestamp = Stopwatch.GetTimestamp();
        var outcome = CrestAppsTelemetry.OutcomeOk;
        var receivedText = false;

        await using var enumerator = updates.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    outcome = CrestAppsTelemetry.GetOutcome(ex);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

                    throw;
                }

                if (!receivedText && !string.IsNullOrEmpty(update.Text))
                {
                    receivedText = true;

                    AITelemetry.OrchestrationTimeToFirstToken.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, orchestratorTag);
                }

                yield return update;

                // An iterator resumes with its consumer's Activity.Current, so the span is restored before the
                // orchestrator produces the next update; otherwise its later spans would lose their parent. With
                // nothing listening there is no span, and the consumer's own one is left as it is.
                if (activity is not null)
                {
                    Activity.Current = activity;
                }
            }
        }
        finally
        {
            AITelemetry.OrchestrationDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                orchestratorTag,
                new KeyValuePair<string, object>(CrestAppsTelemetry.OutcomeTag, outcome));
        }
    }
}
