using System.IO.Compression;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Names, types and packages the files the reading tools offer as downloads.
/// </summary>
internal static class PdfDownloads
{
    /// <summary>
    /// The media type of a zip archive.
    /// </summary>
    public const string ZipContentType = "application/zip";

    private static readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".md"] = "text/markdown",
        [".txt"] = "text/plain",
        [".html"] = "text/html",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".zip"] = ZipContentType,
    };

    /// <summary>
    /// Gets the name a PDF's downloads are named after: its name without the extension, cleaned for use in
    /// a file name.
    /// </summary>
    /// <param name="source">The PDF.</param>
    /// <returns>For example <c>report</c> for <c>report.pdf</c>.</returns>
    public static string BaseName(PdfSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return PdfToolContext.SanitizeName(source.Name);
    }

    /// <summary>
    /// Gets the media type a download of a given extension is served with.
    /// </summary>
    /// <param name="extension">The extension, with its dot.</param>
    /// <returns>The media type, or <see langword="null"/> to let the host infer it.</returns>
    public static string ContentTypeFor(string extension)
    {
        return extension is not null && _contentTypes.TryGetValue(extension, out var contentType)
            ? contentType
            : null;
    }

    /// <summary>
    /// Makes a file name for a download: the name asked for, or a default, cleaned and given the extension
    /// its format needs.
    /// </summary>
    /// <param name="requested">The name asked for, or <see langword="null"/>.</param>
    /// <param name="fallback">The name used when none was asked for.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <returns>The file name.</returns>
    public static string FileName(string requested, string fallback, string extension)
    {
        var name = string.IsNullOrWhiteSpace(requested)
            ? fallback
            : Path.GetFileName(requested.Trim());

        var current = Path.GetExtension(name);

        // A short extension is taken for the one the name was given, and replaced by the one the format needs.
        if (!string.IsNullOrEmpty(current) && current.Length <= 6 && current[1..].All(char.IsLetterOrDigit))
        {
            name = name[..^current.Length];
        }

        return PdfToolContext.SanitizeName(name) + extension;
    }

    /// <summary>
    /// Packs files into a zip archive, giving repeated names a number.
    /// </summary>
    /// <param name="entries">The files, by name.</param>
    /// <returns>The archive.</returns>
    public static byte[] Zip(IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, bytes) in entries)
            {
                var entryName = UniqueEntryName(used, string.IsNullOrWhiteSpace(name) ? "file" : name);

                // Pictures that are already compressed gain nothing from being compressed again.
                var level = entryName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || entryName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Optimal;

                var entry = archive.CreateEntry(entryName, level);

                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes the sentence that hands a download marker to the model.
    /// </summary>
    /// <param name="marker">The <c>[doc:N]</c> marker.</param>
    /// <returns>The instruction.</returns>
    public static string DescribeMarker(string marker)
    {
        return $"Download: {marker} — include this marker in your answer exactly as written; it becomes the download link. Do not write your own link.";
    }

    private static string UniqueEntryName(HashSet<string> used, string name)
    {
        if (used.Add(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var number = 2; ; number++)
        {
            var candidate = FormattableString.Invariant($"{stem}-{number}{extension}");

            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
