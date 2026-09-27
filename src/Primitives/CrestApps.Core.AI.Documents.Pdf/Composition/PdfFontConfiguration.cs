using CrestApps.Core.AI.Documents.Pdf.Services;
using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Points PDFsharp at the host's fonts, once per process.
/// </summary>
internal static class PdfFontConfiguration
{
    private static int _configured;

    /// <summary>
    /// Configures font resolution if it has not been configured yet.
    /// </summary>
    public static void Ensure()
    {
        if (Interlocked.Exchange(ref _configured, 1) != 0)
        {
            return;
        }

        // Resolve fonts from the host operating system. On non-Windows hosts that lack a custom resolver,
        // fall back to a font discovered in the system font directories so PDF generation works out of the
        // box on Linux/macOS servers. Truly font-less environments can still register their own resolver.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;

        if (!OperatingSystem.IsWindows() &&
            GlobalFontSettings.FontResolver is null &&
            SystemFontResolver.TryCreate(out var resolver))
        {
            GlobalFontSettings.FontResolver = resolver;
        }
    }
}
