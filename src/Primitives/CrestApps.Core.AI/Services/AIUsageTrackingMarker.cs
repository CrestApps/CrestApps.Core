namespace CrestApps.Core.AI.Services;

/// <summary>
/// Registered by <c>AddCoreAIUsageTracking()</c>. The AI client factory meters the clients it creates only when
/// this marker is registered, so a host that never opts in pays nothing for metering.
/// </summary>
internal sealed class AIUsageTrackingMarker
{
}