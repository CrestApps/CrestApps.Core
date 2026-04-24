using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class SampleHostContentRootResolverTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"crestapps-content-root-{Guid.NewGuid():N}");

    [Fact]
    public void ResolveContentRoot_ReturnsProjectDirectory_WhenProjectFileExistsAboveBaseDirectory()
    {
        var projectDirectory = Path.Combine(_rootDirectory, "src", "Startup", "CrestApps.Core.Mvc.Web");
        var outputDirectory = Path.Combine(projectDirectory, "bin", "Debug", "net10.0");

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "CrestApps.Core.Mvc.Web.csproj"), "<Project />");

        var contentRoot = SampleHostContentRootResolver.ResolveContentRoot(
            "CrestApps.Core.Mvc.Web.csproj",
            outputDirectory,
            _rootDirectory);

        Assert.Equal(projectDirectory, contentRoot);
    }

    [Fact]
    public void ResolveContentRoot_ReturnsFallback_WhenProjectFileDoesNotExist()
    {
        var outputDirectory = Path.Combine(_rootDirectory, "bin", "Debug", "net10.0");

        Directory.CreateDirectory(outputDirectory);

        var contentRoot = SampleHostContentRootResolver.ResolveContentRoot(
            "CrestApps.Core.Mvc.Web.csproj",
            outputDirectory,
            _rootDirectory);

        Assert.Equal(_rootDirectory, contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
