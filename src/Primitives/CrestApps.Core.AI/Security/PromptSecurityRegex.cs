using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Holds the match timeout shared by the prompt and output security patterns, including the custom blocked patterns.
/// </summary>
internal static class PromptSecurityRegex
{
    /// <summary>
    /// The longest time, in milliseconds, one security pattern may take to match one input.
    /// </summary>
    /// <remarks>
    /// The timeout is measured in wall-clock time, so it also counts the time the first match of a
    /// built-in pattern spends compiling the pattern's generated code, and any time the thread waits for
    /// a garbage collection or a busy CPU. A few hundred milliseconds is not enough for those on a loaded
    /// host, where a safe prompt then fails with a <see cref="RegexMatchTimeoutException"/>. A normal
    /// match of a built-in pattern takes a few microseconds, so one second still stops a pathological input.
    /// </remarks>
    public const int MatchTimeoutMilliseconds = 1000;
}
