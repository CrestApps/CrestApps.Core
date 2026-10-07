using System.Reflection;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Word;
using CrestApps.Core.AI.Documents.Word.Services;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordAgentRegistrationTests
{
    private static readonly string[] _toolNames = typeof(WordToolNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue())
        .ToArray();

    [Fact]
    public async Task ToolScopeHandler_WordAgent_KeepsEveryToolInScope()
    {
        var handler = new WordAgentToolScopeHandler();
        var agentContext = new OrchestrationContext { CompletionContext = new AICompletionContext() };
        var otherContext = new OrchestrationContext { CompletionContext = new AICompletionContext() };

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile { Name = WordAgentProvider.AgentName }, agentContext), TestContext.Current.CancellationToken);
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile { Name = "another-agent" }, otherContext), TestContext.Current.CancellationToken);

        Assert.Equal(WordAgentProvider.ToolNames.Order(), agentContext.MustIncludeTools.Order());
        Assert.Contains(WordToolNames.FormatWordDocument, agentContext.MustIncludeTools);
        Assert.Empty(otherContext.MustIncludeTools);
    }

    [Fact]
    public async Task FormatWordDocument_NameMatchingNothingWithOneDocument_UsesThatDocument()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new { name = "Project Falcon Annual Report", content = new object[] { new { type = "heading", text = "Summary", level = 1 } } });

        var formatted = await host.InvokeAsync(new FormatWordDocumentTool(), new { document = "report", theme = new { preset = "modern" } });
        var removed = await host.InvokeAsync(new RemoveWordContentTool(), new { document = "report", scope = "document" });

        Assert.Contains("the conversation's only document, \"Project Falcon Annual Report\", was used", formatted, StringComparison.Ordinal);
        Assert.Contains("There is no Word document named \"report\"", removed, StringComparison.Ordinal);
        Assert.Single((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public void AddWordTools_RegistersEveryWordToolName_AsAHiddenWordTool()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        WordToolRegistrations.AddWordTools(services);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value.Tools;

        Assert.Equal(32, _toolNames.Length);

        foreach (var name in _toolNames)
        {
            Assert.True(tools.TryGetValue(name, out var entry), $"{name} is registered.");
            Assert.Equal(WordToolRegistrations.Category, entry.Category);
            Assert.False(entry.IsSelectable(), $"{name} is hidden from the tool picker.");
        }

        Assert.Equal(_toolNames.Length, tools.Count);
    }

    [Fact]
    public void Agent_RunsEveryWordTool()
    {
        Assert.Empty(_toolNames.Except(WordAgentProvider.ToolNames));
    }

    [Fact]
    public async Task GetProfiles_ReturnsTheAlwaysAvailableSystemAgent_UnlessDisabled()
    {
        var templates = new PromptTemplateService("You work on Word documents.");
        var enabled = new WordAgentProvider(templates, Options.Create(new WordAgentOptions()));
        var disabled = new WordAgentProvider(templates, Options.Create(new WordAgentOptions { Enabled = false }));

        var agent = Assert.Single(await enabled.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));

        Assert.Equal(WordAgentProvider.AgentName, agent.Name);
        Assert.True(agent.TryGet<AgentMetadata>(out var metadata));
        Assert.Equal(AgentAvailability.AlwaysAvailable, metadata.Availability);
        Assert.True(metadata.IsSystem);
        Assert.True(agent.TryGet<AIProfileMetadata>(out var profile));
        Assert.Equal("You work on Word documents.", profile.SystemMessage);
        Assert.True(agent.TryGet<FunctionInvocationMetadata>(out var functions));
        Assert.Contains(WordToolNames.CreateWordDocument, functions.Names);

        Assert.Empty(await enabled.GetProfilesAsync(AIProfileType.Chat, TestContext.Current.CancellationToken));
        Assert.Empty(await disabled.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupHandler_DeletesTheConversationWorkspace()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new { name = "draft", content = new object[] { new { type = "paragraph", text = "Hello" } } });

        Assert.NotEmpty((await host.LoadWorkspaceAsync()).Documents);

        var store = host.Services.GetRequiredService<IWordWorkspaceStore>();

        await new WordWorkspaceCleanupHandler(store).CleanupAsync(host.Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction, TestContext.Current.CancellationToken);

        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    private sealed class PromptTemplateService : ITemplateService
    {
        private readonly string _prompt;

        public PromptTemplateService(string prompt)
        {
            _prompt = prompt;
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prompt);
        }

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Template>>([]);
        }

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Template>(null);
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prompt);
        }
    }
}
