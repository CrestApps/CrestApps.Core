using CrestApps.Core.AI.FileSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Indexers;

/// <summary>
/// Covers which configuration section <see cref="FileSourceOptions"/> is read from.
/// </summary>
/// <remarks>
/// <para>
/// The section was renamed from <c>CrestApps:Indexers</c> to <c>CrestApps:AI:FileSources</c> when the
/// package was renamed. A configuration key lives in places this repository cannot see -- user secrets,
/// environment variables, a deployed <c>appsettings.json</c> -- so the old spelling is still read.
/// </para>
/// <para>
/// Getting this wrong fails quietly. An unbound <see cref="FileSourceOptions"/> has no allowed roots, and a
/// file source whose root is not allowed refuses every folder rather than erroring, so the symptom is an
/// ingestion run that finds nothing.
/// </para>
/// </remarks>
public sealed class FileSourceConfigurationSectionTests
{
    [Fact]
    public void CurrentSection_IsRead()
    {
        var options = Resolve(new()
        {
            ["CrestApps:AI:FileSources:AllowedLocalRoots:0"] = "/srv/current",
            ["CrestApps:AI:FileSources:MaxItemsPerRun"] = "17",
        });

        Assert.Equal(["/srv/current"], options.AllowedLocalRoots);
        Assert.Equal(17, options.MaxItemsPerRun);
    }

    [Fact]
    public void DeprecatedSection_IsStillRead_WhenTheCurrentOneIsAbsent()
    {
        var options = Resolve(new()
        {
            ["CrestApps:Indexers:AllowedLocalRoots:0"] = "/srv/legacy",
        });

        Assert.Equal(["/srv/legacy"], options.AllowedLocalRoots);
    }

    [Fact]
    public void CurrentSection_WinsOutright_WhenBothArePresent()
    {
        var options = Resolve(new()
        {
            ["CrestApps:Indexers:AllowedLocalRoots:0"] = "/srv/legacy-first",
            ["CrestApps:Indexers:AllowedLocalRoots:1"] = "/srv/legacy-second",
            ["CrestApps:AI:FileSources:AllowedLocalRoots:0"] = "/srv/current",
        });

        // Not a merge. Layering the two would bind by index, so the second legacy root would survive a host
        // that had deliberately shortened its list to one entry.
        Assert.Equal(["/srv/current"], options.AllowedLocalRoots);
    }

    [Fact]
    public void NeitherSection_LeavesTheDefaults()
    {
        var options = Resolve([]);

        Assert.Empty(options.AllowedLocalRoots);
    }

    private static FileSourceOptions Resolve(Dictionary<string, string> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreFileSources(configuration);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<FileSourceOptions>>()
            .Value;
    }
}
