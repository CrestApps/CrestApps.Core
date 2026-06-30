using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// A best-effort <see cref="IFontResolver"/> that serves a single sans-serif font located in the host
/// operating system's font directories. It allows PDF generation to work on non-Windows hosts (where
/// PDFsharp cannot read system fonts on its own) without requiring the application to bundle a font, by
/// reusing a font already installed on the machine and simulating bold/italic styles from it.
/// </summary>
internal sealed class SystemFontResolver : IFontResolver
{
    private const string FaceName = "CrestAppsSystemFallback";

    private static readonly string[] _preferredFontFiles =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/Library/Fonts/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ];

    private static readonly string[] _fontDirectories =
    [
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "/System/Library/Fonts",
    ];

    private readonly byte[] _fontData;

    private SystemFontResolver(byte[] fontData)
    {
        _fontData = fontData;
    }

    /// <summary>
    /// Attempts to create a resolver from the first usable font discovered on the host.
    /// </summary>
    /// <param name="resolver">The created resolver when a font was found.</param>
    /// <returns><see langword="true"/> when a usable font was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreate(out SystemFontResolver resolver)
    {
        var fontFile = FindFontFile();

        if (fontFile is null)
        {
            resolver = null;

            return false;
        }

        try
        {
            resolver = new SystemFontResolver(File.ReadAllBytes(fontFile));

            return true;
        }
        catch (IOException)
        {
            resolver = null;

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            resolver = null;

            return false;
        }
    }

    /// <summary>
    /// Returns the font data for the requested face.
    /// </summary>
    /// <param name="faceName">The face name.</param>
    public byte[] GetFont(string faceName)
    {
        return _fontData;
    }

    /// <summary>
    /// Resolves the requested typeface to the single discovered font, simulating bold and italic styles.
    /// </summary>
    /// <param name="familyName">The requested font family.</param>
    /// <param name="isBold">Whether a bold face was requested.</param>
    /// <param name="isItalic">Whether an italic face was requested.</param>
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(FaceName, isBold, isItalic);
    }

    private static string FindFontFile()
    {
        foreach (var candidate in _preferredFontFiles)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var directory in _fontDirectories)
        {
            var match = FindFontInDirectory(directory);

            if (match is not null)
            {
                return match;
            }
        }

        var userFontsDirectory = GetUserFontsDirectory();

        return userFontsDirectory is not null
            ? FindFontInDirectory(userFontsDirectory)
            : null;
    }

    private static string FindFontInDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var fontFiles = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories).ToList();

            if (fontFiles.Count == 0)
            {
                return null;
            }

            return fontFiles.FirstOrDefault(IsPreferredSansRegular) ?? fontFiles[0];
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsPreferredSansRegular(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name.Contains("Sans", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Mono", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Bold", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Italic", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUserFontsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(home))
        {
            return null;
        }

        var fonts = Path.Combine(home, ".fonts");

        if (Directory.Exists(fonts))
        {
            return fonts;
        }

        var localFonts = Path.Combine(home, ".local", "share", "fonts");

        if (Directory.Exists(localFonts))
        {
            return localFonts;
        }

        var macFonts = Path.Combine(home, "Library", "Fonts");

        return Directory.Exists(macFonts)
            ? macFonts
            : null;
    }
}
