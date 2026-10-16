using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfConversationChartTests
{
    private const string TopItems = """[chart:{"type":"bar","data":{"labels":["Alpha [v2]","Beta"],"datasets":[{"label":"Total","data":[12,7]}]},"options":{"indexAxis":"y","plugins":{"title":{"text":"Top items {grouped}"}}}}]""";

    private const string Amounts = """[chart:{"type":"bar","data":{"labels":["Gamma"],"datasets":[{"label":"Amount","data":[30]}]}}]""";

    [Fact]
    public async Task OrchestrationHandler_KeepsTheAgentsCoreToolsThroughRelevanceScoping()
    {
        var handler = new PdfDocumentOrchestrationHandler(
            Mock.Of<CrestApps.Core.Templates.Services.ITemplateService>(),
            workspaceStore: null,
            new ServiceCollection().BuildServiceProvider());

        var context = new OrchestrationContext { CompletionContext = new AICompletionContext() };

        using var scope = AIInvocationScope.Begin();
        scope.Context.AgentInvocationDepth = 1;

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile { Name = PdfAgentProvider.AgentName }, context),
            TestContext.Current.CancellationToken);

        Assert.Contains("export_pdf", context.MustIncludeTools);
        Assert.Contains("preview_pdf", context.MustIncludeTools);
        Assert.Contains("add_pdf_content", context.MustIncludeTools);
    }

    [Fact]
    public void FindMarkers_ReadsEveryMarker_WhenTheirStringsHoldBracketsAndBraces()
    {
        var markers = PdfChartJsReader.FindMarkers($"Here are the charts:\n\n{TopItems}\n\nand\n{Amounts}\n[chart:not json]");

        Assert.Equal(2, markers.Count);
        Assert.True(PdfChartJsReader.TryRead(markers[0], out var first));
        Assert.Equal(["Alpha [v2]", "Beta"], first.Labels);
        Assert.Equal("Top items {grouped}", first.Title);
        Assert.True(PdfChartJsReader.TryRead(markers[1], out var second));
        Assert.Equal([30d], second.Series[0].Values.Select(value => value.Value));
    }

    [Fact]
    public async Task PrepareAsync_FailsTheCall_WhenAChartHasNothingToPlot()
    {
        var blocks = new List<PdfBlockDefinition>
        {
            new() { Type = "heading", Text = "Sales by Region" },
            new() { Type = "chart", Chart = new PdfChartDefinition { Title = "Sales by Region" } },
        };

        var exception = await Assert.ThrowsAsync<PdfToolException>(() =>
            PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken));

        Assert.Contains("no data to plot", exception.Message);
    }

    [Fact]
    public async Task PrepareAsync_ReadsAChartWrittenFlatOnItsBlock()
    {
        // The shape a live model sent: the chart's fields on the block instead of inside "chart".
        var blocks = PdfDefinitionJson.FromNode<List<PdfBlockDefinition>>(System.Text.Json.Nodes.JsonNode.Parse(
            """[{"type":"chart","chart_type":"horizontal_bar","title":"By rank","labels":["A","B"],"series":[{"name":"Rank","values":[1,2]}]},{"type":"image","source":"[chart:{\"type\":\"bar\",\"data\":{\"labels\":[\"C\"],\"datasets\":[{\"data\":[3]}]}}]"}]"""));

        await PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken);

        Assert.Equal("horizontal_bar", blocks[0].Chart.ChartType);
        Assert.Equal("By rank", blocks[0].Chart.Title);
        Assert.Equal(["A", "B"], blocks[0].Chart.Labels);
        Assert.Equal([1d, 2d], blocks[0].Chart.Series[0].Values.Select(value => value.Value));
        Assert.Null(blocks[0].Unrecognized);
        Assert.Equal(PdfBlockTypes.Chart, blocks[1].Type);
        Assert.Equal(["C"], blocks[1].Chart.Labels);
    }

    [Fact]
    public async Task PrepareAsync_ReadsAFlatConversationChartNumber()
    {
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var store = new Mock<IChatInteractionPromptStore>();

        store.Setup(candidate => candidate.GetPromptsAsync("interaction-1"))
            .ReturnsAsync([new ChatInteractionPrompt { Role = ChatRole.Assistant, Text = TopItems }]);

        var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(interaction);

        var blocks = PdfDefinitionJson.FromNode<List<PdfBlockDefinition>>(System.Text.Json.Nodes.JsonNode.Parse(
            """[{"type":"chart","conversation_chart":1,"title":"Chosen title"}]"""));

        await PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, services, TestContext.Current.CancellationToken);

        Assert.Equal("Chosen title", blocks[0].Chart.Title);
        Assert.Equal([12d, 7d], blocks[0].Chart.Series[0].Values.Select(value => value.Value));
    }

    [Fact]
    public async Task PrepareAsync_KeepsAnEmptyChart_WhenChartDataIsNotRequired()
    {
        var blocks = new List<PdfBlockDefinition>
        {
            new() { Type = "chart", Chart = new PdfChartDefinition() },
        };

        await PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken, requireChartData: false);

        Assert.Single(blocks);
    }

    [Fact]
    public async Task PrepareAsync_DrawsAChart_WhenAnImageBlockPointsAtAChartMarker()
    {
        var blocks = new List<PdfBlockDefinition>
        {
            new() { Type = "image", Image = new PdfImageDefinition { Source = Amounts, Caption = "Amounts" } },
        };

        await PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken);

        var block = Assert.Single(blocks);
        Assert.Equal(PdfBlockTypes.Chart, block.Type);
        Assert.Null(block.Image);
        Assert.Equal(["Gamma"], block.Chart.Labels);
        Assert.Equal("Amounts", block.Chart.Caption);
    }

    [Fact]
    public async Task PrepareAsync_ReadsAConversationChart_FromTheEarlierAnswers()
    {
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var store = new Mock<IChatInteractionPromptStore>();

        store.Setup(candidate => candidate.GetPromptsAsync("interaction-1"))
            .ReturnsAsync(
            [
                new ChatInteractionPrompt { Role = ChatRole.User, Text = $"ignored {Amounts}", CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new ChatInteractionPrompt { Role = ChatRole.Assistant, Text = $"Here:\n{TopItems}\n{Amounts}", CreatedUtc = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc) },
            ]);

        var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(interaction);

        var blocks = new List<PdfBlockDefinition>
        {
            new() { Type = "chart", Chart = new PdfChartDefinition { ConversationChart = 1 } },
            new() { Type = "image", Image = new PdfImageDefinition { Source = "chart:2" } },
        };

        await PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, services, TestContext.Current.CancellationToken);

        Assert.Equal("horizontal_bar", blocks[0].Chart.ChartType);
        Assert.Equal("Top items {grouped}", blocks[0].Chart.Title);
        Assert.Equal([12d, 7d], blocks[0].Chart.Series[0].Values.Select(value => value.Value));
        Assert.Null(blocks[0].Chart.ConversationChart);
        Assert.Equal(PdfBlockTypes.Chart, blocks[1].Type);
        Assert.Equal(["Gamma"], blocks[1].Chart.Labels);

        var listed = PdfConversationCharts.Describe(await PdfConversationCharts.GetAsync(services));

        Assert.Contains("1. \"Top items {grouped}\"", listed);
        Assert.Contains("2. (untitled)", listed);
    }

    [Fact]
    public async Task PrepareAsync_ListsTheConversationCharts_WhenTheNumberIsWrong()
    {
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var store = new Mock<IChatInteractionPromptStore>();

        store.Setup(candidate => candidate.GetPromptsAsync("interaction-1"))
            .ReturnsAsync([new ChatInteractionPrompt { Role = ChatRole.Assistant, Text = TopItems }]);

        var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(interaction);

        var blocks = new List<PdfBlockDefinition>
        {
            new() { Type = "chart", Chart = new PdfChartDefinition { ConversationChart = 3 } },
        };

        var exception = await Assert.ThrowsAsync<PdfToolException>(() =>
            PdfBlockPreparer.PrepareAsync(new PdfDocumentDefinition(), blocks, services, TestContext.Current.CancellationToken));

        Assert.Contains("no conversation chart 3", exception.Message);
        Assert.Contains("Top items {grouped}", exception.Message);
    }
}
