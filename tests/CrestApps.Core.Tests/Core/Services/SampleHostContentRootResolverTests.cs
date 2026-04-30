using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class SampleHostContentRootResolverTests : IDisposable
{
    private const string FakeProjectFileName = "FakeTestProject.Web.csproj";

    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"crestapps-content-root-{Guid.NewGuid():N}");

    [Fact]
    public void ResolveContentRoot_ReturnsProjectDirectory_WhenProjectFileExistsAboveBaseDirectory()
    {
        var projectDirectory = Path.Combine(_rootDirectory, "src", "Startup", "FakeTestProject.Web");
        var outputDirectory = Path.Combine(projectDirectory, "bin", "Debug", "net10.0");

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, FakeProjectFileName), "<Project />");

        var contentRoot = SampleHostContentRootResolver.ResolveContentRoot(
            FakeProjectFileName,
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
            FakeProjectFileName,
            outputDirectory,
            _rootDirectory);

        Assert.Equal(_rootDirectory, contentRoot);
    }

    [Fact]
    public void ResolveContentRoot_ReturnsProjectDirectory_WhenProjectFileExistsBelowFallbackDirectory()
    {
        var projectDirectory = Path.Combine(_rootDirectory, "src", "Startup", "FakeTestProject.Web");
        var unrelatedOutputDirectory = Path.Combine(_rootDirectory, "artifacts", "aspire", "MvcWeb");

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(unrelatedOutputDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, FakeProjectFileName), "<Project />");

        var contentRoot = SampleHostContentRootResolver.ResolveContentRoot(
            FakeProjectFileName,
            unrelatedOutputDirectory,
            _rootDirectory);

        Assert.Equal(projectDirectory, contentRoot);
    }

    [Fact]
    public void ResolveContentRoot_FindsProjectViaSolutionRoot_WhenBaseDirectoryIsDeepInRepoTree()
    {
        // Simulates VS + Aspire: base directory is deep under repo root,
        // project is elsewhere, and a .slnx marker identifies the repo root.
        var projectDirectory = Path.Combine(_rootDirectory, "src", "Startup", "FakeTestProject.Web");
        var deepBaseDirectory = Path.Combine(_rootDirectory, "src", "Startup", "AppHost", "bin", "Debug", "net10.0");
        var unrelatedFallback = Path.Combine(_rootDirectory, "artifacts", "something");

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(deepBaseDirectory);
        Directory.CreateDirectory(unrelatedFallback);
        File.WriteAllText(Path.Combine(projectDirectory, FakeProjectFileName), "<Project />");
        File.WriteAllText(Path.Combine(_rootDirectory, "FakeTestSolution.slnx"), "");

        var contentRoot = SampleHostContentRootResolver.ResolveContentRoot(
            FakeProjectFileName,
            deepBaseDirectory,
            unrelatedFallback);

        Assert.Equal(projectDirectory, contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

