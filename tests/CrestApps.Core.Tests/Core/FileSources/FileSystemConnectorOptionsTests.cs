using CrestApps.Core.AI.FileSources.Connectors;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers the boundary the host puts around the file-system connector: which folders it may read, and how a
/// configured folder is resolved against it.
/// </summary>
/// <remarks>
/// This is the whole of what stops an administrator with access to the File Sources screen reading any file
/// the host process can open, so every way out of the boundary is worth a test of its own.
/// </remarks>
public sealed class FileSystemConnectorOptionsTests
{
    private static readonly string Base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "crestapps-fs-options"));

    /// <summary>
    /// Verifies that a folder inside an allowed root resolves, and comes back as a full path.
    /// </summary>
    [Fact]
    public void FolderInsideAnAllowedRoot_Resolves()
    {
        var options = Create("App_Data/file-sources");

        Assert.True(options.TryResolveRoot("App_Data/file-sources/contracts", out var resolved, out _));
        Assert.Equal(Path.Combine(Base, "App_Data", "file-sources", "contracts"), resolved);
    }

    /// <summary>
    /// Verifies that the allowed root itself resolves, since a host that allows one folder means that folder.
    /// </summary>
    [Fact]
    public void TheAllowedRootItself_Resolves()
    {
        var options = Create("App_Data/file-sources");

        Assert.True(options.TryResolveRoot("App_Data/file-sources", out _, out _));
    }

    /// <summary>
    /// Verifies that a folder outside every allowed root is refused.
    /// </summary>
    [Fact]
    public void FolderOutsideEveryAllowedRoot_IsRefused()
    {
        var options = Create("App_Data/file-sources");

        Assert.False(options.TryResolveRoot("App_Data/secrets", out _, out var reason));
        Assert.Contains("configured to read", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a path climbing out of the allowed root is refused.
    /// </summary>
    [Fact]
    public void PathClimbingOutOfTheRoot_IsRefused()
    {
        var options = Create("App_Data/file-sources");

        Assert.False(options.TryResolveRoot("App_Data/file-sources/../../secrets", out _, out var reason));
        Assert.Contains("'..'", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a path climbing out and back in is refused too.
    /// </summary>
    /// <remarks>
    /// Containment alone would accept this, because it normalizes to a folder inside the root. It is refused
    /// because a path that leaves the sandbox and returns is never what someone meant to type, and accepting
    /// it would mean the check depends on where the two paths happen to land.
    /// </remarks>
    [Fact]
    public void PathClimbingOutAndBackIn_IsRefused()
    {
        var options = Create("App_Data/file-sources");

        Assert.False(options.TryResolveRoot("App_Data/file-sources/../file-sources/contracts", out _, out var reason));
        Assert.Contains("'..'", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a Windows-style separator does not hide a traversal.
    /// </summary>
    [Fact]
    public void BackslashTraversal_IsRefused()
    {
        var options = Create("App_Data/file-sources");

        Assert.False(options.TryResolveRoot(@"App_Data\file-sources\..\..\secrets", out _, out _));
    }

    /// <summary>
    /// Verifies that a folder whose name merely begins with two dots is allowed, since the segments are
    /// compared whole.
    /// </summary>
    [Fact]
    public void FolderNamedLikeATraversal_IsAllowed()
    {
        var options = Create("App_Data/file-sources");

        Assert.True(options.TryResolveRoot("App_Data/file-sources/..archive", out _, out _));
    }

    /// <summary>
    /// Verifies that a sibling whose name starts with the allowed root's name is refused, because the
    /// comparison is by path segment rather than by string prefix.
    /// </summary>
    [Fact]
    public void SiblingSharingAPrefixWithTheRoot_IsRefused()
    {
        var options = Create("App_Data/file-sources");

        Assert.False(options.TryResolveRoot("App_Data/file-sources-private", out _, out _));
    }

    /// <summary>
    /// Verifies that a host with no allowed roots reads nothing, and says so as its own reason.
    /// </summary>
    /// <remarks>
    /// That is the safe default for a host that has not thought about it: registering the connector grants
    /// no folder on its own.
    /// </remarks>
    [Fact]
    public void NoAllowedRoots_RefusesEverything()
    {
        var options = new FileSystemConnectorOptions { BasePath = Base };

        Assert.False(options.TryResolveRoot("App_Data/file-sources", out _, out var reason));
        Assert.Contains("not configured to read any folder", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an absolute folder is checked against the allowed roots like any other.
    /// </summary>
    [Fact]
    public void AbsolutePath_IsCheckedAgainstTheAllowedRoots()
    {
        var options = Create("App_Data/file-sources");
        var inside = Path.Combine(Base, "App_Data", "file-sources", "contracts");

        Assert.True(options.TryResolveRoot(inside, out _, out _));
        Assert.False(options.TryResolveRoot(Path.Combine(Base, "App_Data", "secrets"), out _, out _));
    }

    /// <summary>
    /// Verifies that a relative path is measured from the configured base rather than the process's current
    /// directory, which is not the content root under IIS or a Windows service.
    /// </summary>
    [Fact]
    public void RelativePath_IsResolvedAgainstTheBasePath()
    {
        var options = Create("App_Data/file-sources");

        Assert.True(options.TryResolveRoot("App_Data/file-sources/contracts", out var resolved, out _));
        Assert.StartsWith(Base, resolved, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that naming no folder reads the one folder the host allows.
    /// </summary>
    /// <remarks>
    /// Leaving the folder empty is the configuration a reader with nothing to narrow down arrives at, and
    /// it used to be refused as "No folder is configured" -- so the simplest setup was the one that
    /// silently ingested nothing.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolderNamed_ResolvesToTheSingleAllowedRoot(string rootPath)
    {
        var options = Create("App_Data/file-sources");

        Assert.True(options.TryResolveRoot(rootPath, out var resolved, out var reason));
        Assert.Null(reason);
        Assert.Equal(Path.Combine(Base, "App_Data", "file-sources"), resolved);
    }

    /// <summary>
    /// Verifies that naming no folder is refused when the host allows several, since there is no single
    /// folder to fall back to and reading one nobody pointed at is worse than saying so.
    /// </summary>
    [Fact]
    public void NoFolderNamed_IsRefusedWhenSeveralRootsAreAllowed()
    {
        var options = Create("App_Data/file-sources", "App_Data/other-files");

        Assert.False(options.TryResolveRoot(null, out _, out var reason));
        Assert.Contains("more than one folder", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that naming no folder is refused when the host allows none, which is the same refusal a
    /// named folder gets.
    /// </summary>
    [Fact]
    public void NoFolderNamed_IsRefusedWhenNoRootIsAllowed()
    {
        var options = new FileSystemConnectorOptions { BasePath = Base };

        Assert.False(options.TryResolveRoot(null, out _, out var reason));
        Assert.Contains("not configured to read any folder", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a blank entry in the allowed roots is not counted, so a host whose configuration left
    /// a hole still gets the one real folder it named.
    /// </summary>
    [Fact]
    public void NoFolderNamed_IgnoresBlankAllowedRoots()
    {
        var options = Create("", "App_Data/file-sources", "   ");

        Assert.True(options.TryResolveRoot(null, out var resolved, out _));
        Assert.Equal(Path.Combine(Base, "App_Data", "file-sources"), resolved);
    }

    private static FileSystemConnectorOptions Create(params string[] allowedRoots)
    {
        var options = new FileSystemConnectorOptions { BasePath = Base };

        foreach (var root in allowedRoots)
        {
            options.AllowedRoots.Add(root);
        }

        return options;
    }
}
