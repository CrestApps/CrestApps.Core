using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Where a conversation's presentation workspace lives in the document file store.
/// </summary>
/// <remarks>
/// Beside the tabular workspace, under the conversation's own folder, so removing a conversation's files
/// removes its decks too and one conversation can never resolve into another's folder.
/// </remarks>
internal static partial class PresentationWorkspaceStorage
{
    /// <summary>
    /// The name of the record of the workspace's decks.
    /// </summary>
    public const string StateFileName = "workspace.json";

    /// <summary>
    /// Returns the store-relative folder of a conversation's workspace.
    /// </summary>
    /// <param name="referenceType">The conversation's reference type.</param>
    /// <param name="referenceId">The conversation's identifier.</param>
    /// <returns>The folder, or <see langword="null"/> when the scope is incomplete or unsafe.</returns>
    public static string GetFolder(string referenceType, string referenceId)
    {
        if (!IsSafeSegment(referenceType) || !IsSafeSegment(referenceId))
        {
            return null;
        }

        return string.Join('/', "documents", referenceType, referenceId, "data", "presentations");
    }

    /// <summary>
    /// Returns the store-relative path of a file in a workspace folder.
    /// </summary>
    /// <param name="folder">The workspace folder.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The path.</returns>
    public static string Combine(string folder, string fileName)
    {
        return folder + "/" + fileName;
    }

    private static bool IsSafeSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && SafeSegmentExpression().IsMatch(value);
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafeSegmentExpression();
}
