namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// What one pass over the due sources did.
/// </summary>
/// <param name="Considered">How many configured sources were looked at.</param>
/// <param name="Ran">How many were due and were run.</param>
/// <param name="Failed">How many threw while running. Their failure is logged, not rethrown.</param>
public readonly record struct FileSourceSchedulerResult(int Considered, int Ran, int Failed);
