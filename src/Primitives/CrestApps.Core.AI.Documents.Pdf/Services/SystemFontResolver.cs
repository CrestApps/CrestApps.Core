using System.Collections.Concurrent;
using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Finds the fonts installed on the host that PDF text is written with: the family a document asks for
/// when it is installed, otherwise the installed font that stands in for it — Liberation Sans, Serif and
/// Mono first, because they have the widths of Arial, Times New Roman and Courier New, so a document lays
/// out as it does on Windows — and the bold, italic and bold italic faces of that font.
/// </summary>
/// <remarks>
/// On Windows the fonts are found by the names stored in the font files, since their file names are
/// abbreviations; on Linux and macOS by the file names in the font directories. A style is simulated only
/// when the font has no face for it, and the text still comes out with a real bold or italic face wherever
/// one is installed, so it reads as bold or italic to anything that reads the PDF back. When no font of a
/// kind is installed, any installed font is used rather than failing the document.
/// </remarks>
internal sealed class SystemFontResolver
{
    private const int MaxFontFiles = 5000;

    private static readonly string[] _fontDirectories =
    [
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "/System/Library/Fonts",
    ];

    // Families are compared by their letters and digits only, lowercased: "Liberation Sans" and
    // "LiberationSans-Bold.ttf" both read "liberationsans".
    private static readonly string[] _sansFamilies = ["liberationsans", "arimo", "arial", "helvetica", "dejavusans", "freesans", "notosans", "opensans", "roboto", "ubuntu"];
    private static readonly string[] _serifFamilies = ["liberationserif", "tinos", "timesnewroman", "times", "dejavuserif", "freeserif", "notoserif"];
    private static readonly string[] _monoFamilies = ["liberationmono", "cousine", "couriernew", "courier", "dejavusansmono", "freemono", "notosansmono", "ubuntumono"];

    private static readonly (string Suffix, FontStyle Style)[] _styleSuffixes =
    [
        ("bolditalic", FontStyle.BoldItalic),
        ("boldoblique", FontStyle.BoldItalic),
        ("bi", FontStyle.BoldItalic),
        ("bold", FontStyle.Bold),
        ("bd", FontStyle.Bold),
        ("italic", FontStyle.Italic),
        ("oblique", FontStyle.Italic),
        ("it", FontStyle.Italic),
        ("regular", FontStyle.Regular),
        ("book", FontStyle.Regular),
        ("roman", FontStyle.Regular),
    ];

    private readonly Dictionary<string, Dictionary<FontStyle, string>> _families;
    private readonly Dictionary<string, (string Path, int FaceIndex)> _files;
    private readonly ConcurrentDictionary<string, byte[]> _data = new(StringComparer.Ordinal);
    private readonly string _fallbackFamily;

    private SystemFontResolver(
        Dictionary<string, Dictionary<FontStyle, string>> families,
        Dictionary<string, (string Path, int FaceIndex)> files,
        string fallbackFamily)
    {
        _families = families;
        _files = files;
        _fallbackFamily = fallbackFamily;
    }

    /// <summary>
    /// The styles a font file can hold.
    /// </summary>
    internal enum FontStyle
    {
        /// <summary>
        /// Upright, normal weight.
        /// </summary>
        Regular,

        /// <summary>
        /// Bold.
        /// </summary>
        Bold,

        /// <summary>
        /// Italic or oblique.
        /// </summary>
        Italic,

        /// <summary>
        /// Bold and italic.
        /// </summary>
        BoldItalic,
    }

    /// <summary>
    /// Creates a resolver over the fonts installed on this machine.
    /// </summary>
    /// <param name="resolver">The resolver, when any font is installed.</param>
    /// <returns><see langword="true"/> when a font was found.</returns>
    public static bool TryCreate(out SystemFontResolver resolver)
    {
        resolver = OperatingSystem.IsWindows()
            ? FromFaces(FindWindowsFaces())
            : FromFiles(FindFontFiles());

        return resolver is not null;
    }

    /// <summary>
    /// Creates a resolver over the given font files, reading each file's family and style from its name.
    /// </summary>
    /// <param name="paths">The TrueType files.</param>
    /// <returns>The resolver, or <see langword="null"/> when there is no file.</returns>
    internal static SystemFontResolver FromFiles(IEnumerable<string> paths)
    {
        var faces = new List<(string Path, int FaceIndex, string FaceName, string Family, FontStyle Style)>();

        foreach (var path in paths ?? [])
        {
            var faceName = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrEmpty(faceName))
            {
                continue;
            }

            var (family, style) = Parse(faceName);

            faces.Add((path, 0, faceName, family, style));
        }

        return Create(faces);
    }

    /// <summary>
    /// Creates a resolver over faces whose family and style were read from the font files themselves.
    /// </summary>
    /// <param name="faces">Each face's file, index in the file, family and style.</param>
    /// <returns>The resolver, or <see langword="null"/> when there is no face.</returns>
    internal static SystemFontResolver FromFaces(IEnumerable<(string Path, int FaceIndex, string Family, bool Bold, bool Italic)> faces)
    {
        var entries = new List<(string Path, int FaceIndex, string FaceName, string Family, FontStyle Style)>();

        foreach (var (path, faceIndex, family, bold, italic) in faces ?? [])
        {
            var style = (bold, italic) switch
            {
                (true, true) => FontStyle.BoldItalic,
                (true, false) => FontStyle.Bold,
                (false, true) => FontStyle.Italic,
                _ => FontStyle.Regular,
            };

            entries.Add((path, faceIndex, Path.GetFileNameWithoutExtension(path) + "#" + faceIndex.ToString(CultureInfo.InvariantCulture), Normalize(family), style));
        }

        return Create(entries);
    }

    /// <summary>
    /// Reads the file of a face: a font file of its own, taken out of its collection when it is in one.
    /// </summary>
    /// <param name="faceName">The face name <see cref="ResolveTypeface"/> returned.</param>
    /// <returns>The font file.</returns>
    public byte[] GetFont(string faceName)
    {
        if (!_files.ContainsKey(faceName ?? string.Empty))
        {
            faceName = Face(_fallbackFamily, FontStyle.Regular).FaceName;
        }

        return _data.GetOrAdd(faceName, name =>
        {
            var (path, faceIndex) = _files[name];

            return PdfTrueTypeFile.ExtractFace(File.ReadAllBytes(path), faceIndex);
        });
    }

    /// <summary>
    /// Chooses the installed face for a family and style.
    /// </summary>
    /// <param name="familyName">The family the document asks for.</param>
    /// <param name="isBold">Whether it asks for bold.</param>
    /// <param name="isItalic">Whether it asks for italic.</param>
    /// <returns>The face, and the styles that have to be simulated because the font has no face for them.</returns>
    public SystemFontFace ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var requested = Normalize(familyName);
        var family = Choose(requested);
        var wanted = (isBold, isItalic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular,
        };

        var (faceName, style) = Face(family, wanted);
        var hasBold = style is FontStyle.Bold or FontStyle.BoldItalic;
        var hasItalic = style is FontStyle.Italic or FontStyle.BoldItalic;

        return new SystemFontFace(faceName, isBold && !hasBold, isItalic && !hasItalic, family == requested);
    }

    private static SystemFontResolver Create(IEnumerable<(string Path, int FaceIndex, string FaceName, string Family, FontStyle Style)> faces)
    {
        var families = new Dictionary<string, Dictionary<FontStyle, string>>(StringComparer.Ordinal);
        var files = new Dictionary<string, (string Path, int FaceIndex)>(StringComparer.Ordinal);

        foreach (var (path, faceIndex, faceName, family, style) in faces)
        {
            if (family.Length == 0 || files.ContainsKey(faceName))
            {
                continue;
            }

            files[faceName] = (path, faceIndex);

            if (!families.TryGetValue(family, out var styles))
            {
                styles = [];
                families[family] = styles;
            }

            styles.TryAdd(style, faceName);
        }

        if (files.Count == 0)
        {
            return null;
        }

        var fallback = _sansFamilies.FirstOrDefault(families.ContainsKey)
            ?? families.Keys.Where(key => key.Contains("sans", StringComparison.Ordinal) && !key.Contains("mono", StringComparison.Ordinal)).Order(StringComparer.Ordinal).FirstOrDefault()
            ?? families.Keys.Order(StringComparer.Ordinal).First();

        return new SystemFontResolver(families, files, fallback);
    }

    private string Choose(string requested)
    {
        if (requested.Length > 0 && _families.ContainsKey(requested))
        {
            return requested;
        }

        var candidates = IsMono(requested)
            ? _monoFamilies
            : IsSerif(requested)
                ? _serifFamilies
                : _sansFamilies;

        return candidates.FirstOrDefault(_families.ContainsKey) ?? _fallbackFamily;
    }

    private (string FaceName, FontStyle Style) Face(string family, FontStyle wanted)
    {
        var styles = _families[family];

        if (styles.TryGetValue(wanted, out var exact))
        {
            return (exact, wanted);
        }

        // Without a bold italic face, the bold one is closer than the italic one: weight shows more.
        var nearest = wanted switch
        {
            FontStyle.BoldItalic => new[] { FontStyle.Bold, FontStyle.Italic, FontStyle.Regular },
            FontStyle.Bold or FontStyle.Italic => [FontStyle.Regular],
            _ => [FontStyle.Bold, FontStyle.Italic, FontStyle.BoldItalic],
        };

        foreach (var style in nearest)
        {
            if (styles.TryGetValue(style, out var face))
            {
                return (face, style);
            }
        }

        var any = styles.First();

        return (any.Value, any.Key);
    }

    private static (string Family, FontStyle Style) Parse(string faceName)
    {
        var normalized = Normalize(faceName);

        foreach (var (suffix, style) in _styleSuffixes)
        {
            // A suffix only counts as a style when something is left for the family: "Bold.ttf" is not a family.
            if (normalized.Length > suffix.Length + 2 && normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                // "Italic" inside "BoldItalic" was matched first; short suffixes need the separator the file
                // name wrote, so "Credit.ttf" is not read as an italic "Cred".
                if (suffix.Length <= 2 && !HasSeparatorBefore(faceName, suffix.Length))
                {
                    continue;
                }

                return (normalized[..^suffix.Length], style);
            }
        }

        return (normalized, FontStyle.Regular);
    }

    private static bool HasSeparatorBefore(string faceName, int suffixLength)
    {
        var index = faceName.Length - suffixLength - 1;

        return index >= 0 && faceName[index] is '-' or '_' or ' ';
    }

    private static string Normalize(string name)
    {
        return new string((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static bool IsMono(string family)
    {
        return family.Contains("mono", StringComparison.Ordinal) ||
            family.Contains("courier", StringComparison.Ordinal) ||
            family.Contains("consol", StringComparison.Ordinal) ||
            family.Contains("menlo", StringComparison.Ordinal) ||
            family.Contains("code", StringComparison.Ordinal);
    }

    private static bool IsSerif(string family)
    {
        return (family.Contains("serif", StringComparison.Ordinal) && !family.Contains("sansserif", StringComparison.Ordinal)) ||
            family.Contains("times", StringComparison.Ordinal) ||
            family.Contains("georgia", StringComparison.Ordinal) ||
            family.Contains("garamond", StringComparison.Ordinal) ||
            family.Contains("cambria", StringComparison.Ordinal) ||
            family.Contains("palatino", StringComparison.Ordinal) ||
            family.Contains("bookman", StringComparison.Ordinal);
    }

    private static List<string> FindFontFiles()
    {
        var files = new List<string>();
        var directories = _fontDirectories.ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            directories.Add(Path.Combine(home, ".fonts"));
            directories.Add(Path.Combine(home, ".local", "share", "fonts"));
            directories.Add(Path.Combine(home, "Library", "Fonts"));
        }

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories).Take(MaxFontFiles - files.Count));
            }
            catch (IOException)
            {
                // A directory that cannot be read is skipped; the others may still hold fonts.
            }
            catch (UnauthorizedAccessException)
            {
                // Likewise.
            }

            if (files.Count >= MaxFontFiles)
            {
                break;
            }
        }

        return files;
    }

    private static List<(string Path, int FaceIndex, string Family, bool Bold, bool Italic)> FindWindowsFaces()
    {
        var faces = new List<(string Path, int FaceIndex, string Family, bool Bold, bool Italic)>();
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        };

        var files = 0;

        foreach (var directory in directories.Where(directory => !string.IsNullOrEmpty(directory) && Directory.Exists(directory)))
        {
            IEnumerable<string> paths;

            try
            {
                paths = Directory.EnumerateFiles(directory, "*.tt?", SearchOption.TopDirectoryOnly)
                    .Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A directory that cannot be read is skipped; the other may still hold fonts.
                continue;
            }

            foreach (var path in paths)
            {
                if (++files > MaxFontFiles)
                {
                    return faces;
                }

                try
                {
                    using var stream = File.OpenRead(path);

                    foreach (var (index, family, bold, italic) in PdfTrueTypeFile.ReadFaces(stream))
                    {
                        faces.Add((path, index, family, bold, italic));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException)
                {
                    // A font that cannot be read is skipped, the way the system skips it.
                }
            }
        }

        return faces;
    }
}
