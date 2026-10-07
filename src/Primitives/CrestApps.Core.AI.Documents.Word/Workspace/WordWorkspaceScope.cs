using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// The conversation a Word workspace belongs to.
/// </summary>
/// <param name="ReferenceId">The chat session or chat interaction identifier.</param>
/// <param name="ReferenceType">The reference type.</param>
internal readonly record struct WordWorkspaceScope(string ReferenceId, string ReferenceType)
{
    /// <summary>
    /// Gets the storage folder the workspace is kept in.
    /// </summary>
    /// <remarks>
    /// Hashed so a conversation identifier never becomes a path segment on its own.
    /// </remarks>
    public string Folder
    {
        get
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ReferenceType + "|" + ReferenceId));

            return "documents/word-workspaces/" + Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Gets the path of the workspace state file.
    /// </summary>
    public string StatePath => Folder + "/workspace.json.gz";
}
