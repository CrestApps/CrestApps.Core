using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Gives the file a preview shows as a download in the same turn.
/// </summary>
/// <remarks>
/// A preview tool used to register only its pictures, and a download existed only when the model also called
/// the export tool. It often did not — the instructions told it to skip the export when the user "only asked to
/// see" the file — so the reader was shown the pages of a file they could not get. A preview now always offers
/// the file it shows. The download is kept for the turn by file and version, so an export of the same version
/// in the same turn hands back the same marker rather than a second copy of the file, and a later preview of a
/// newer version replaces the copy an earlier preview offered instead of listing both.
/// </remarks>
internal static class PreviewDownloads
{
    private const string ItemsKey = nameof(PreviewDownloads) + ".Downloads";

    /// <summary>
    /// Returns the download marker for a version of a file, exporting it the first time it is asked for in
    /// this turn.
    /// </summary>
    /// <param name="file">What identifies the file within the conversation, such as its working name.</param>
    /// <param name="version">What identifies the version of the file that is shown.</param>
    /// <param name="export">Writes the file as a download and returns its marker, or <see langword="null"/>.</param>
    /// <returns>The <c>[doc:N]</c> marker, or <see langword="null"/> when no download could be given.</returns>
    public static async Task<string> OfferAsync(string file, string version, Func<Task<string>> export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var invocation = AIInvocationScope.Current;

        if (invocation is null || string.IsNullOrEmpty(file))
        {
            return null;
        }

        var downloads = GetDownloads(invocation);

        if (downloads.TryGetValue(file, out var existing) &&
            string.Equals(existing.Version, version, StringComparison.Ordinal) &&
            invocation.ToolReferences.ContainsKey(existing.Marker))
        {
            return existing.Marker;
        }

        var marker = await export();

        if (string.IsNullOrEmpty(marker))
        {
            return null;
        }

        Replace(invocation, downloads, file, new Download(version, marker, OfferedByPreview: true));

        return marker;
    }

    /// <summary>
    /// Gets the download already given in this turn for a version of a file.
    /// </summary>
    /// <param name="file">What identifies the file within the conversation.</param>
    /// <param name="version">What identifies the version of the file.</param>
    /// <param name="marker">The download marker, when there is one.</param>
    /// <returns><see langword="true"/> when this version of the file was already given as a download.</returns>
    public static bool TryGet(string file, string version, out string marker)
    {
        marker = null;

        var invocation = AIInvocationScope.Current;

        if (invocation is null ||
            string.IsNullOrEmpty(file) ||
            !GetDownloads(invocation).TryGetValue(file, out var existing) ||
            !string.Equals(existing.Version, version, StringComparison.Ordinal) ||
            !invocation.ToolReferences.ContainsKey(existing.Marker))
        {
            return false;
        }

        marker = existing.Marker;

        return true;
    }

    /// <summary>
    /// Records a download an export tool gave, so a preview of the same version in this turn reuses it.
    /// </summary>
    /// <param name="file">What identifies the file within the conversation.</param>
    /// <param name="version">What identifies the version of the file.</param>
    /// <param name="marker">The download marker the export returned.</param>
    public static void Record(string file, string version, string marker)
    {
        var invocation = AIInvocationScope.Current;

        if (invocation is null || string.IsNullOrEmpty(file) || string.IsNullOrEmpty(marker))
        {
            return;
        }

        Replace(invocation, GetDownloads(invocation), file, new Download(version, marker, OfferedByPreview: false));
    }

    /// <summary>
    /// Builds the line a preview tool adds to its answer so the model gives the reader the download too.
    /// </summary>
    /// <param name="marker">The download marker.</param>
    /// <param name="fileName">The name of the file the download holds.</param>
    /// <returns>The instruction, or an empty string when there is no marker.</returns>
    public static string Instructions(string marker, string fileName)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return string.Empty;
        }

        return $"The file shown is also given as the download \"{fileName}\". WRITE THIS DOWNLOAD MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN AFTER THE PICTURE MARKERS: {marker}";
    }

    private static void Replace(AIInvocationContext invocation, Dictionary<string, Download> downloads, string file, Download download)
    {
        // A copy offered only because an earlier version was previewed is out of date once a newer one is
        // given, and listing both would leave the reader to guess which file is the current one. A copy the
        // model exported on purpose is the model's to keep.
        if (downloads.TryGetValue(file, out var previous) &&
            previous.OfferedByPreview &&
            !string.Equals(previous.Marker, download.Marker, StringComparison.OrdinalIgnoreCase))
        {
            invocation.ToolReferences.Remove(previous.Marker);
        }

        downloads[file] = download;
    }

    private static Dictionary<string, Download> GetDownloads(AIInvocationContext invocation)
    {
        if (!invocation.Items.TryGetValue(ItemsKey, out var value) || value is not Dictionary<string, Download> downloads)
        {
            downloads = new Dictionary<string, Download>(StringComparer.OrdinalIgnoreCase);
            invocation.Items[ItemsKey] = downloads;
        }

        return downloads;
    }

    private sealed record Download(string Version, string Marker, bool OfferedByPreview);
}
