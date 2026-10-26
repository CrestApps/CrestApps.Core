using CrestApps.Core.AI.Documents.Pdf.Services;
using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Configures font resolution for the PDF tools that still draw through PDFsharp.
/// </summary>
internal static class PdfFontConfiguration
{
    private static readonly Lock _gate = new();

    private static volatile bool _configured;

    /// <summary>
    /// Configures font resolution once per process.
    /// </summary>
    public static void Ensure()
    {
        if (_configured)
        {
            return;
        }

        lock (_gate)
        {
            if (_configured)
            {
                return;
            }

            // Resolve fonts from the host operating system. On non-Windows hosts that lack a custom resolver,
            // serve the fonts found in the system font directories so PDF generation works out of the box on
            // Linux/macOS servers. Truly font-less environments can still register their own resolver.
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;

            if (!OperatingSystem.IsWindows() &&
                GlobalFontSettings.FontResolver is null &&
                SystemFontResolver.TryCreate(out var resolver))
            {
                GlobalFontSettings.FontResolver = new PdfSharpFontResolver(resolver);
            }

            _configured = true;
        }
    }

    /// <summary>
    /// Serves PDFsharp the fonts <see cref="SystemFontResolver"/> finds.
    /// </summary>
    private sealed class PdfSharpFontResolver : IFontResolver
    {
        private readonly SystemFontResolver _resolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class.
        /// </summary>
        /// <param name="resolver">The system fonts.</param>
        public PdfSharpFontResolver(SystemFontResolver resolver)
        {
            _resolver = resolver;
        }

        /// <inheritdoc/>
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var face = _resolver.ResolveTypeface(familyName, isBold, isItalic);

            return new FontResolverInfo(face.FaceName, face.MustSimulateBold, face.MustSimulateItalic);
        }

        /// <inheritdoc/>
        public byte[] GetFont(string faceName)
        {
            return _resolver.GetFont(faceName);
        }
    }
}
