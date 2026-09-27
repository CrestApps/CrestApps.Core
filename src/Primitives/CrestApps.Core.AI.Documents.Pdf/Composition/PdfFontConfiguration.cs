using CrestApps.Core.AI.Documents.Pdf.Services;
using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Points PDFsharp at the host's fonts, once per process.
/// </summary>
internal static class PdfFontConfiguration
{
    private static readonly Lock _gate = new();

    private static volatile bool _configured;

    /// <summary>
    /// Configures font resolution if it has not been configured yet.
    /// </summary>
    /// <remarks>
    /// A caller that arrives while another is configuring waits for it: returning early would let it draw text
    /// before the resolver is in place, which PDFsharp fails with "No appropriate font found".
    /// </remarks>
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
                GlobalFontSettings.FontResolver = resolver;
            }

            _configured = true;
        }
    }
}
