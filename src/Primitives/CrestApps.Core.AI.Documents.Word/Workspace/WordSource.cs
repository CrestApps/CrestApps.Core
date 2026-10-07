using System.Globalization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// The Word document a tool call works on: a working document in the workspace, or a file the user uploaded.
/// </summary>
internal sealed class WordSource
{
    private WordSource(WordWorkingDocument working, AIDocument upload)
    {
        Working = working;
        Upload = upload;
    }

    /// <summary>
    /// Gets the working document, or <see langword="null"/> for an upload.
    /// </summary>
    public WordWorkingDocument Working { get; }

    /// <summary>
    /// Gets the upload, or <see langword="null"/> for a working document.
    /// </summary>
    public AIDocument Upload { get; }

    /// <summary>
    /// Gets a value indicating whether the source is an upload, which is never changed.
    /// </summary>
    public bool IsUpload => Upload is not null;

    /// <summary>
    /// Gets the name tools refer to the source by.
    /// </summary>
    public string Name => Working?.Name ?? Upload?.FileName;

    /// <summary>
    /// Gets a key that changes whenever the content does, for caching work done on it within a turn.
    /// </summary>
    public string Key => Working is not null
        ? "working|" + Working.Name + "|" + Working.Version.ToString(CultureInfo.InvariantCulture)
        : "upload|" + Upload?.ItemId;

    /// <summary>
    /// Creates the source of a working document.
    /// </summary>
    /// <param name="working">The working document.</param>
    /// <returns>The source.</returns>
    public static WordSource ForWorking(WordWorkingDocument working)
    {
        ArgumentNullException.ThrowIfNull(working);

        return new WordSource(working, null);
    }

    /// <summary>
    /// Creates the source of an upload.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <returns>The source.</returns>
    public static WordSource ForUpload(AIDocument upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        return new WordSource(null, upload);
    }

    /// <summary>
    /// Describes the source for a tool's answer.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        return IsUpload
            ? $"uploaded file \"{Upload.FileName}\""
            : string.Create(CultureInfo.InvariantCulture, $"working document \"{Working.Name}\" (version {Working.Version})");
    }
}
