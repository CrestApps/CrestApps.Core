using CrestApps.Core.AI.Documents.Pdf.Services;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class SystemFontResolverTests
{
    private static readonly string[] _installed =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
        "/usr/share/fonts/truetype/roboto/Roboto-Regular.ttf",
        "/usr/share/fonts/truetype/roboto/Roboto-Bold.ttf",
    ];

    [Theory]
    [InlineData("Arial", false, false, "LiberationSans-Regular")]
    [InlineData("Arial", true, false, "LiberationSans-Bold")]
    [InlineData("Helvetica", false, true, "LiberationSans-Italic")]
    [InlineData("Segoe UI", true, true, "LiberationSans-BoldItalic")]
    [InlineData("Courier New", false, false, "LiberationMono-Regular")]
    [InlineData("Roboto", true, false, "Roboto-Bold")]
    public void ResolveTypeface_UsesTheRealFaceForTheStyle(string family, bool bold, bool italic, string face)
    {
        var resolver = SystemFontResolver.FromFiles(_installed);
        var resolved = resolver.ResolveTypeface(family, bold, italic);

        Assert.Equal(face, resolved.FaceName);
        Assert.False(resolved.MustSimulateBold);
        Assert.False(resolved.MustSimulateItalic);
    }

    [Fact]
    public void ResolveTypeface_SimulatesOnlyTheStylesAFontHasNoFaceFor()
    {
        var resolver = SystemFontResolver.FromFiles(_installed);

        // Serif text uses the serif font installed, which has no bold face.
        var serif = resolver.ResolveTypeface("Times New Roman", true, false);

        Assert.Equal("DejaVuSerif", serif.FaceName);
        Assert.True(serif.MustSimulateBold);

        // Roboto has a bold face but no italic one.
        var roboto = resolver.ResolveTypeface("Roboto", true, true);

        Assert.Equal("Roboto-Bold", roboto.FaceName);
        Assert.False(roboto.MustSimulateBold);
        Assert.True(roboto.MustSimulateItalic);
    }

    [Fact]
    public void FromFiles_WithoutFonts_ReturnsNull()
    {
        Assert.Null(SystemFontResolver.FromFiles([]));
    }
}
