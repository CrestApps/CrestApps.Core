using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// The short label a tool hands the model so the host can draw a picture where the label sits.
/// </summary>
/// <remarks>
/// A model asked to reproduce a picture's address reproduces its shape and varies the digits, so the
/// address is never given to it. It writes this label instead, the reference is registered under the very
/// same string, and the host substitutes the picture back in.
/// <para>
/// Every tool that shows a picture draws its numbers from here. The first registration of a key wins, so a
/// second tool restarting at one would silently take over a marker the first had already claimed and the
/// reader would be shown a picture the answer never named.
/// </para>
/// </remarks>
internal static class FigureReferenceMarker
{
    /// <summary>
    /// What a figure marker opens with.
    /// </summary>
    public const string Prefix = "[fig:";

    /// <summary>
    /// Builds the marker for a figure number.
    /// </summary>
    /// <param name="index">The figure number.</param>
    /// <returns>The marker, for example <c>[fig:1]</c>.</returns>
    public static string Format(int index)
    {
        return $"{Prefix}{index}]";
    }

    /// <summary>
    /// Returns the first figure number nothing in the invocation has claimed yet.
    /// </summary>
    /// <param name="context">The invocation context, or <see langword="null"/> when there is none.</param>
    /// <returns>The first free marker number.</returns>
    public static int NextIndex(AIInvocationContext context)
    {
        if (context is null)
        {
            return 1;
        }

        var highest = 0;

        foreach (var key in context.ToolReferences.Keys)
        {
            if (key.Length <= Prefix.Length + 1 ||
                !key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
                key[^1] != ']')
            {
                continue;
            }

            if (int.TryParse(key.AsSpan(Prefix.Length, key.Length - Prefix.Length - 1), out var index) && index > highest)
            {
                highest = index;
            }
        }

        return highest + 1;
    }
}
