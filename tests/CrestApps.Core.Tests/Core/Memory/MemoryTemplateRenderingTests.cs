using CrestApps.Core.AI.Memory;
using CrestApps.Core.Templates.Extensions;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Memory;

/// <summary>
/// Renders the memory prompt templates through the real Fluid engine. Fluid denies member access to types that
/// are not registered, so an argument passed as an anonymous object renders as blank text; a fake template
/// service cannot catch that, which is how the retrieved-memory block shipped with empty entries.
/// </summary>
public sealed class MemoryTemplateRenderingTests
{
    [Fact]
    public async Task MemoryContextHeader_RendersEveryRetrievedMemory()
    {
        var templateService = CreateTemplateService();

        var text = await templateService.RenderAsync(
            MemoryConstants.TemplateIds.MemoryContextHeader,
            new Dictionary<string, object>
            {
                ["results"] = new List<Dictionary<string, object>>
                {
                    new()
                    {
                        ["Name"] = "favorite_color",
                        ["Description"] = "The user's favorite color.",
                        ["Content"] = "teal",
                        ["UpdatedUtc"] = "2026-10-30T00:00:00.0000000Z",
                    },
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("Memory: favorite_color", text);
        Assert.Contains("Description: The user's favorite color.", text);
        Assert.Contains("Content: teal", text);
    }

    [Fact]
    public async Task ConversationMemoryContext_RendersSummaryVerbatimMessagesAndRecalledTurns()
    {
        var templateService = CreateTemplateService();

        var text = await templateService.RenderAsync(
            MemoryConstants.TemplateIds.ConversationMemoryContext,
            new Dictionary<string, object>
            {
                ["summary"] = "Project code name is BLUE-HERON-77.",
                ["pendingMessages"] = new List<Dictionary<string, object>>
                {
                    new() { ["Role"] = "User", ["Text"] = "The deadline is in March." },
                },
                ["recalledTurns"] = new List<Dictionary<string, object>>
                {
                    new() { ["Number"] = 1, ["UserText"] = "The budget is 48,500 dollars.", ["AssistantText"] = "Noted." },
                },
                ["searchToolName"] = "search_conversation",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("Project code name is BLUE-HERON-77.", text);
        Assert.Contains("User: The deadline is in March.", text);
        Assert.Contains("--- Turn 1", text);
        Assert.Contains("User: The budget is 48,500 dollars.", text);
        Assert.Contains("Assistant: Noted.", text);
        Assert.Contains("call `search_conversation`", text);
    }

    [Fact]
    public async Task ConversationMemoryContext_WithOnlyASummary_LeavesOutTheEmptySections()
    {
        var templateService = CreateTemplateService();

        var text = await templateService.RenderAsync(
            MemoryConstants.TemplateIds.ConversationMemoryContext,
            new Dictionary<string, object>
            {
                ["summary"] = "A summary.",
                ["pendingMessages"] = new List<Dictionary<string, object>>(),
                ["recalledTurns"] = new List<Dictionary<string, object>>(),
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("A summary.", text);
        Assert.DoesNotContain("Messages just before", text);
        Assert.DoesNotContain("Earlier turns", text);
        Assert.DoesNotContain("search", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SummarizationAndPastConversationTemplates_Render()
    {
        var templateService = CreateTemplateService();

        var summarization = await templateService.RenderAsync(MemoryConstants.TemplateIds.ConversationSummarization, cancellationToken: TestContext.Current.CancellationToken);
        var past = await templateService.RenderAsync(
            MemoryConstants.TemplateIds.PastConversationsAvailability,
            new Dictionary<string, object> { ["searchToolName"] = "search_past_conversations", ["listToolName"] = "list_recent_conversations" },
            TestContext.Current.CancellationToken);

        Assert.Contains("Copy identifiers and figures verbatim", summarization);
        Assert.Contains("`search_past_conversations`", past);
        Assert.Contains("`list_recent_conversations`", past);
    }

    private static ITemplateService CreateTemplateService()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddTemplating();
        services.AddTemplatesFromAssembly(typeof(CrestApps.Core.AI.ServiceCollectionExtensions).Assembly);

        return services.BuildServiceProvider().GetRequiredService<ITemplateService>();
    }
}
