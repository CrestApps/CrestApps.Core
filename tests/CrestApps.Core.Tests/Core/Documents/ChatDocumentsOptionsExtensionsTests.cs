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
}
