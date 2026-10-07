using System.Reflection;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfAgentRegistrationTests
{
    private static readonly string[] _toolNames = typeof(PdfToolNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue())
        .ToArray();

    [Fact]
    public void AddPdfTools_RegistersEveryPdfToolName_AsAHiddenPdfTool()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        PdfToolRegistrations.AddPdfTools(services);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value.Tools;

        Assert.Equal(53, _toolNames.Length);

        foreach (var name in _toolNames)
        {
            Assert.True(tools.TryGetValue(name, out var entry), $"{name} is registered.");
            Assert.Equal(PdfToolRegistrations.Category, entry.Category);
            Assert.False(entry.IsSelectable(), $"{name} is hidden from the tool picker.");
        }

        Assert.Equal(_toolNames.Length, tools.Count);
    }

    [Fact]
    public void Agent_RunsEveryPdfTool()
    {
        Assert.Empty(_toolNames.Except(PdfAgentProvider.ToolNames));
    }

    [Fact]
    public async Task GetProfiles_ReturnsTheAlwaysAvailableSystemAgent_UnlessDisabled()
    {
        var templates = new PromptTemplateService("You work on PDFs.");
        var enabled = new PdfAgentProvider(templates, Options.Create(new PdfAgentOptions()));
        var disabled = new PdfAgentProvider(templates, Options.Create(new PdfAgentOptions { Enabled = false }));

        var agent = Assert.Single(await enabled.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));

        Assert.Equal(PdfAgentProvider.AgentName, agent.Name);
        Assert.True(agent.TryGet<AgentMetadata>(out var metadata));
        Assert.Equal(AgentAvailability.AlwaysAvailable, metadata.Availability);
        Assert.True(metadata.IsSystem);
        Assert.True(agent.TryGet<AIProfileMetadata>(out var profile));
        Assert.Equal("You work on PDFs.", profile.SystemMessage);
        Assert.True(agent.TryGet<FunctionInvocationMetadata>(out var functions));
        Assert.Contains(PdfToolNames.CreatePdf, functions.Names);

        Assert.Empty(await enabled.GetProfilesAsync(AIProfileType.Chat, TestContext.Current.CancellationToken));
        Assert.Empty(await disabled.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupHandler_DeletesTheConversationWorkspace()
    {
        using var host = new PdfToolTestHost();
        await host.InvokeAsync(new CreatePdfTool(), new { name = "draft", blocks = new object[] { new { type = "paragraph", text = "Hello" } } });

        Assert.NotEmpty((await host.LoadWorkspaceAsync()).Documents);

        var store = host.Services.GetRequiredService<IPdfWorkspaceStore>();

        await new PdfWorkspaceCleanupHandler(store).CleanupAsync(host.Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction, TestContext.Current.CancellationToken);

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
