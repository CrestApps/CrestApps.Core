using CrestApps.Core.AI.Documents.OpenXml.Word;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// A Word document kept in a conversation's workspace: one the agent created, or a working copy of an upload.
/// The file itself is the source of truth; this records where it is stored and what was done to it.
/// </summary>
internal sealed class WordWorkingDocument
{
    /// <summary>
    /// Gets or sets the name tools refer to the document by.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the stored path of the current version of the file.
    /// </summary>
    public string BlobPath { get; set; }

    /// <summary>
    /// Gets or sets the size of the current version, in bytes.
    /// </summary>
    public long ByteLength { get; set; }

    /// <summary>
    /// Gets or sets the version number, raised by every change.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the file name of the upload the document was copied from, if any.
    /// </summary>
    public string SourceFileName { get; set; }

    /// <summary>
    /// Gets or sets the document id of the upload the document was copied from, if any.
    /// </summary>
    public string SourceDocumentId { get; set; }

    /// <summary>
    /// Gets or sets the design the agent builds the document with. A copy of an upload has none until its look
    /// is changed, and content added to it follows its own styles.
    /// </summary>
    public WordDesign Design { get; set; }

    /// <summary>
    /// Gets or sets when the document was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the document last changed.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets what was done to the document, oldest first.
    /// </summary>
    public List<string> History { get; set; } = [];

    /// <summary>
    /// Gets or sets the version that was last exported, so an unchanged document is not exported twice.
    /// </summary>
    public int? ExportedVersion { get; set; }

    /// <summary>
    /// Records a change in the document's history, keeping the most recent fifty.
    /// </summary>
    /// <param name="change">What was done.</param>
    public void Record(string change)
    {
        if (string.IsNullOrWhiteSpace(change))
        {
            return;
        }

        History.Add(change.Trim());

        if (History.Count > 50)
        {
            History.RemoveRange(0, History.Count - 50);
        }
    }
}
