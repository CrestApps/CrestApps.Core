using System.Collections.Concurrent;
using CrestApps.Core.AI.Documents.Pdf.Services;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// The fonts the host can write PDF text with, found once per process and shared.
/// </summary>
/// <remarks>
/// A family that is not installed is written with the installed font that stands in for it. A host
/// without any font, such as a bare container, writes with the standard PDF fonts every reader has, which
/// cover Western European text.
/// </remarks>
internal static class PdfFontLibrary
{
    private static readonly Lazy<SystemFontResolver> _resolver = new(Discover, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, PdfFontProgram> _programs = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), PdfFont> _fonts = new();

    /// <summary>
    /// Gets a value indicating whether the host has fonts installed, so text is embedded in the fonts asked for.
    /// </summary>
    public static bool HasInstalledFonts => _resolver.Value is not null;

    /// <summary>
    /// Returns the font that writes a family in a style.
    /// </summary>
    /// <param name="family">The family, as <c>Arial</c>.</param>
    /// <param name="bold">Whether to write bold.</param>
    /// <param name="italic">Whether to write italic.</param>
    /// <returns>The font; never <see langword="null"/>.</returns>
    public static PdfFont Get(string family, bool bold = false, bool italic = false)
    {
        return _fonts.GetOrAdd((family ?? string.Empty, bold, italic), key => Create(key.Family, key.Bold, key.Italic));
    }

    /// <summary>
    /// Returns whether a family is installed under its own name.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <returns><see langword="true"/> when the host has the family.</returns>
    public static bool IsInstalled(string family)
    {
        return _resolver.Value?.ResolveTypeface(family, false, false).IsRequestedFamily == true;
    }

    private static SystemFontResolver Discover()
    {
        SystemFontResolver.TryCreate(out var resolver);

        return resolver;
    }

    private static PdfFont Create(string family, bool bold, bool italic)
    {
        var resolver = _resolver.Value;

        if (resolver is null)
        {
            return PdfFont.FromStandard(family, bold, italic);
        }

        var face = resolver.ResolveTypeface(family, bold, italic);
        var program = _programs.GetOrAdd(face.FaceName, name => PdfFontProgram.TryLoad(resolver.GetFont(name)));

        return program is null
            ? PdfFont.FromStandard(family, bold, italic)
            : PdfFont.FromProgram(program, family, bold, italic, face.MustSimulateBold, face.MustSimulateItalic);
    }
}
