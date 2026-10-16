using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.Tests.Core.Documents.Generation;

public sealed class PreviewDownloadsTests
{
    [Fact]
    public async Task OfferAsync_SameVersionTwice_ExportsOnce()
    {
        using var scope = AIInvocationScope.Begin();
        var exports = 0;

        var first = await PreviewDownloads.OfferAsync("pdf|brief", "1", () => Export(scope.Context, ref exports));
        var second = await PreviewDownloads.OfferAsync("pdf|brief", "1", () => Export(scope.Context, ref exports));

        Assert.Equal("[doc:1]", first);
        Assert.Equal(first, second);
        Assert.Equal(1, exports);
    }

    [Fact]
    public async Task OfferAsync_NewerVersion_WithdrawsTheCopyAnEarlierPreviewOffered()
    {
        using var scope = AIInvocationScope.Begin();
        var exports = 0;

        var first = await PreviewDownloads.OfferAsync("pdf|brief", "1", () => Export(scope.Context, ref exports));
        var second = await PreviewDownloads.OfferAsync("pdf|brief", "2", () => Export(scope.Context, ref exports));

        Assert.Equal("[doc:2]", second);
        Assert.False(scope.Context.ToolReferences.ContainsKey(first));
        Assert.True(scope.Context.ToolReferences.ContainsKey(second));
    }

    [Fact]
    public async Task OfferAsync_AfterAnExportOfAnotherVersion_KeepsTheExportedFile()
    {
        using var scope = AIInvocationScope.Begin();
        var exports = 0;

        var exported = await Export(scope.Context, ref exports);
        PreviewDownloads.Record("pdf|brief", "1", exported);

        var offered = await PreviewDownloads.OfferAsync("pdf|brief", "2", () => Export(scope.Context, ref exports));

        // The model exported that file on purpose; only a copy a preview offered is withdrawn.
        Assert.NotEqual(exported, offered);
        Assert.True(scope.Context.ToolReferences.ContainsKey(exported));
        Assert.True(scope.Context.ToolReferences.ContainsKey(offered));
    }

    [Fact]
    public async Task TryGet_ReturnsTheOfferedDownload_OnlyForThatVersion()
    {
        using var scope = AIInvocationScope.Begin();
        var exports = 0;

        var offered = await PreviewDownloads.OfferAsync("word|memo", "working|memo|3", () => Export(scope.Context, ref exports));

        Assert.True(PreviewDownloads.TryGet("word|memo", "working|memo|3", out var marker));
        Assert.Equal(offered, marker);
        Assert.False(PreviewDownloads.TryGet("word|memo", "working|memo|4", out _));
        Assert.False(PreviewDownloads.TryGet("word|other", "working|memo|3", out _));
    }

    [Fact]
    public async Task OfferAsync_ExportGivesNoMarker_ReturnsNull_AndRemembersNothing()
    {
        using var scope = AIInvocationScope.Begin();

        var marker = await PreviewDownloads.OfferAsync("pdf|brief", "1", () => Task.FromResult<string>(null));

        Assert.Null(marker);
        Assert.False(PreviewDownloads.TryGet("pdf|brief", "1", out _));
    }

    [Fact]
    public async Task OfferAsync_WithoutAnInvocation_DoesNotExport()
    {
        var exported = false;

        var marker = await PreviewDownloads.OfferAsync("pdf|brief", "1", () =>
        {
            exported = true;

            return Task.FromResult("[doc:1]");
        });

        Assert.Null(marker);
        Assert.False(exported);
    }

    [Fact]
    public void Instructions_NamesTheFileAndTheMarker()
    {
        var text = PreviewDownloads.Instructions("[doc:3]", "brief.pdf");

        Assert.Contains("\"brief.pdf\"", text, StringComparison.Ordinal);
        Assert.EndsWith("[doc:3]", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, PreviewDownloads.Instructions(null, "brief.pdf"));
    }

    // Registers a download the way the generated document service does, and counts the files written.
    private static Task<string> Export(AIInvocationContext invocation, ref int exports)
    {
        exports++;

        var index = invocation.NextReferenceIndex();
        var marker = $"[doc:{index}]";

        invocation.ToolReferences[marker] = new AICompletionReference
        {
            Title = "brief.pdf",
            Index = index,
            IsGenerated = true,
        };

        return Task.FromResult(marker);
    }
}
