using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularExportSignalTests
{
    /// <summary>
    /// Verifies that a completed export is visible to the rest of the invocation, which is what lets
    /// the file-creation tool recognize that the real file already exists. Observed in a live run: the
    /// model exported the real rows and then authored its own spreadsheet from remembered values,
    /// handing the user the invented one.
    /// </summary>
    [Fact]
    public void Record_ThenTryGetLast_ReturnsTheExport()
    {
        using var scope = AIInvocationScope.Begin();

        Assert.False(TabularExportSignal.TryGetLast(out _));

        TabularExportSignal.Record("variance.xlsx", "[doc:1]");

        Assert.True(TabularExportSignal.TryGetLast(out var result));
        Assert.Equal("variance.xlsx", result.FileName);
        Assert.Equal("[doc:1]", result.Marker);
    }

    /// <summary>
    /// Verifies that the signal does not outlive its invocation, so an export in one turn never blocks
    /// a legitimate file in the next.
    /// </summary>
    [Fact]
    public void Record_DoesNotLeakAcrossInvocations()
    {
        using (var scope = AIInvocationScope.Begin())
        {
            TabularExportSignal.Record("variance.xlsx", "[doc:1]");
            Assert.True(TabularExportSignal.TryGetLast(out _));
        }

        using (var scope = AIInvocationScope.Begin())
        {
            Assert.False(TabularExportSignal.TryGetLast(out _));
        }
    }

    /// <summary>
    /// Verifies that recording outside an invocation is a no-op rather than a failure, since the tools
    /// are also exercised outside a live conversation.
    /// </summary>
    [Fact]
    public void Record_WithoutAnInvocation_IsIgnored()
    {
        TabularExportSignal.Record("variance.xlsx", "[doc:1]");

        Assert.False(TabularExportSignal.TryGetLast(out _));
    }
}
