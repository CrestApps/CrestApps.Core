using CrestApps.Core.AI.Documents.Models;

namespace CrestApps.Core.Tests.Core.Documents;

public sealed class ChatDocumentsOptionsExtensionsTests
{
    [Fact]
    public void GetAllowedFileExtensions_WithVisionEnabled_IncludesSupportedImageFormats()
    {
        var options = new ChatDocumentsOptions();
        options.Add(".pdf");
        options.Add(".txt");

        var extensions = options.GetAllowedFileExtensions(includeVisionImages: true);

        Assert.Contains(".pdf", extensions);
        Assert.Contains(".txt", extensions);
        Assert.Contains(".png", extensions);
        Assert.Contains(".jpg", extensions);
        Assert.Contains(".webp", extensions);
    }

    [Fact]
    public void IsAllowedFileExtension_OnlyAllowsVisionImagesWhenEnabled()
    {
        var options = new ChatDocumentsOptions();
        options.Add(".pdf");

        Assert.False(options.IsAllowedFileExtension(".png"));
        Assert.True(options.IsAllowedFileExtension(".png", includeVisionImages: true));
    }

    [Fact]
    public void IsTabularFileExtension_ReturnsTrue_ForAllowedNonEmbeddableExtension()
    {
        var options = new ChatDocumentsOptions();
        options.Add(".csv", embeddable: false, isTabular: true);
        options.Add(".pdf");
        options.Add(".bin", embeddable: false);

        Assert.True(options.IsTabularFileExtension("sales.csv"));
        Assert.True(options.IsTabularFileExtension(".csv"));

        // Embeddable documents are not tabular.
        Assert.False(options.IsTabularFileExtension("report.pdf"));

        // Unknown extensions and images are not tabular.
        Assert.False(options.IsTabularFileExtension("archive.bin"));
        Assert.False(options.IsTabularFileExtension("photo.png"));
        Assert.False(options.IsTabularFileExtension(null));
    }

    [Fact]
    public void Add_ExtractorExtension_RegistersTabularExtension()
    {
        var options = new ChatDocumentsOptions();

        options.Add(new ExtractorExtension(".xlsx", embeddable: true, isTabular: true));

        Assert.Contains(".xlsx", options.AllowedFileExtensions);
        Assert.Contains(".xlsx", options.TabularFileExtensions);
        Assert.DoesNotContain(".xlsx", options.EmbeddableFileExtensions);
    }
}
