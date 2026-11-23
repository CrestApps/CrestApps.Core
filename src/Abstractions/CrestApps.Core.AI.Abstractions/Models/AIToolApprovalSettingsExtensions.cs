using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Reads and writes the one choice the sample profile editors offer: ask before the tools that require an approval
/// because of their kind, or run them all without asking.
/// </summary>
public static class AIToolApprovalSettingsExtensions
{
    private static readonly AIToolKind[] _approvalKinds = [.. Enum.GetValues<AIToolKind>().Where(kind => kind.RequiresApproval())];

    /// <summary>
    /// Determines whether every tool kind that requires an approval runs without asking.
    /// </summary>
    /// <param name="settings">The settings, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when each such kind has a <see cref="ToolApprovalMode.Never"/> rule.</returns>
    public static bool RunsAllWithoutAsking(this AIToolApprovalSettings settings)
    {
        // Read through the same parser the tool pipeline uses, so the editor never shows Ask while the tools run
        // unasked (a stored key in another casing, for example).
        var policy = ToolApprovalPolicy.FromSettings(settings);

        return policy is not null && _approvalKinds.All(policy.KindsRunWithoutAsking.Contains);
    }

    /// <summary>
    /// Sets every tool kind that requires an approval to run without asking, or removes those rules so each call
    /// asks. Rules for other kinds are kept.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="runWithoutAsking">Whether the tools run without asking.</param>
    public static void SetRunAllWithoutAsking(this AIToolApprovalSettings settings, bool runWithoutAsking)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Kinds ??= new(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in _approvalKinds)
        {
            // A stored dictionary can lose its case-insensitive comparer, so remove the kind's key in any casing.
            foreach (var key in settings.Kinds.Keys.Where(key => string.Equals(key, kind.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
            {
                settings.Kinds.Remove(key);
            }

            if (runWithoutAsking)
            {
                settings.Kinds[kind.ToString()] = new ToolApprovalRule { Mode = ToolApprovalMode.Never };
            }
        }
    }
}
