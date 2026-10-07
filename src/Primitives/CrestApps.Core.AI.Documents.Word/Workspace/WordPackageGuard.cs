using System.Globalization;
using System.IO.Compression;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// Checks a <c>.docx</c> file before it is opened, so a file built to expand to far more than its size — a
/// decompression bomb — is refused instead of being unpacked into memory.
/// </summary>
/// <remarks>
/// A <c>.docx</c> file is a zip archive, and the size limit on a document applies to the archive. What the Open
/// XML SDK holds in memory is what its parts expand to, which the archive declares in its directory. The zip
/// reader the SDK opens parts through stops at that declared size — a part that holds more is cut short, not
/// unpacked in full — so the declared sizes bound what opening the file can unpack, and are what is checked.
/// </remarks>
internal static class WordPackageGuard
{
    /// <summary>
    /// The most parts a document may hold.
    /// </summary>
    public const int MaxEntries = 10_000;

    /// <summary>
    /// The most a part larger than <see cref="RatioCheckedEntryBytes"/> may expand to, as a multiple of its
    /// compressed size. Document markup compresses well, but not this well.
    /// </summary>
    public const int MaxCompressionRatio = 250;

    /// <summary>
    /// The unpacked size from which a part's compression ratio is checked; small parts compress unevenly.
    /// </summary>
    public const long RatioCheckedEntryBytes = 10L * 1024 * 1024;

    /// <summary>
    /// Refuses a file whose parts expand beyond the limit, that holds too many parts, or whose parts are
    /// compressed far more tightly than a document's.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="name">How the file is named in a message: its name in quotes, or a phrase such as "The result".</param>
    /// <param name="maxUncompressedBytes">The most bytes the parts may expand to, together.</param>
    /// <exception cref="WordToolException">The file is refused.</exception>
    /// <remarks>
    /// A file that is not a zip archive is let through: opening it reports what is wrong with it.
    /// </remarks>
    public static void EnsureSafe(byte[] bytes, string name, long maxUncompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        ZipArchive archive;

        try
        {
            archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return;
        }

        using (archive)
        {
            var entries = archive.Entries;

            if (entries.Count > MaxEntries)
            {
                throw new WordToolException(string.Create(CultureInfo.InvariantCulture,
                    $"{name} holds {entries.Count:N0} parts, more than the {MaxEntries:N0} a Word document this agent opens may hold."));
            }

            long declared = 0;

            foreach (var entry in entries)
            {
                declared += entry.Length;

                if (entry.Length > maxUncompressedBytes || declared > maxUncompressedBytes)
                {
                    throw TooLarge(name, maxUncompressedBytes);
                }

                if (entry.Length > RatioCheckedEntryBytes &&
                    entry.Length > (long)MaxCompressionRatio * Math.Max(entry.CompressedLength, 1))
                {
                    throw new WordToolException(string.Create(CultureInfo.InvariantCulture,
                        $"{name} is not opened: its part \"{entry.FullName}\" expands from {entry.CompressedLength:N0} to {entry.Length:N0} bytes, far more than a Word document's content compresses."));
                }
            }
        }
    }

    private static WordToolException TooLarge(string name, long maxUncompressedBytes)
    {
        return new WordToolException(string.Create(CultureInfo.InvariantCulture,
            $"{name} expands to more than {maxUncompressedBytes:N0} bytes once unpacked, more than this agent opens."));
    }
}
