using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Verifies how the presentation agent is offered: its profile, its tools, the handlers that clean up after
/// it, and the steering that sends slide decks to it.
/// </summary>
public sealed class PresentationAgentTests
{
    /// <summary>
    /// The agent is a hidden, always available system agent whose tools are all registered.
    /// </summary>
    [Fact]
    public async Task Provider_OffersTheSystemAgentWithRegisteredTools()
    {
        var templates = new Mock<ITemplateService>();
        templates
            .Setup(service => service.RenderAsync(PresentationAgentProvider.SystemPromptTemplateId, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("You are the Presentation Agent.");

        var provider = new PresentationAgentProvider(templates.Object, Options.Create(new PresentationAgentOptions()));
        var profile = Assert.Single(await provider.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));

        Assert.Equal(PresentationAgentProvider.AgentName, profile.Name);
        Assert.True(profile.TryGet<AgentMetadata>(out var agent));
        Assert.Equal(AgentAvailability.AlwaysAvailable, agent.Availability);
        Assert.True(agent.IsSystem);
        Assert.True(profile.TryGet<AIProfileMetadata>(out var metadata));
        Assert.Equal("You are the Presentation Agent.", metadata.SystemMessage);

        var services = new ServiceCollection();
        services.AddCoreAIPresentationAgent();

        using var built = services.BuildServiceProvider();
        var definitions = built.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;

        Assert.True(profile.TryGet<FunctionInvocationMetadata>(out var functions));

        foreach (var name in PresentationAgentProvider.ToolNames.Where(name => name.Contains("presentation", StringComparison.Ordinal) || name.Contains("slide", StringComparison.Ordinal)))
        {
            Assert.Contains(name, functions.Names);
            Assert.True(definitions.Tools.ContainsKey(name), $"The tool '{name}' is not registered.");
        }

        Assert.Equal(41, definitions.Tools.Keys.Count(name => PresentationAgentProvider.ToolNames.Contains(name)));
    }

    /// <summary>
    /// Turning the agent off takes it away.
    /// </summary>
    [Fact]
    public async Task Provider_WhenDisabled_OffersNothing()
    {
        var provider = new PresentationAgentProvider(Mock.Of<ITemplateService>(), Options.Create(new PresentationAgentOptions { Enabled = false }));

        Assert.Empty(await provider.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken));
        Assert.Empty(await provider.GetProfilesAsync(AIProfileType.Chat, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Deleting a conversation deletes its working decks.
    /// </summary>
    [Fact]
    public async Task CleanupHandler_DeletesTheWorkspace()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "One" } } });
        await host.InvokeAsync(PresentationToolNames.UpdateSlide, new { slide = 1, title = "Two" });

        var folder = Path.Combine(host.Root, PresentationWorkspaceStorage.GetFolder(AIReferenceTypes.Document.ChatInteraction, host.Interaction.ItemId));

        Assert.NotEmpty(Directory.GetFiles(folder));

        var handler = new PresentationWorkspaceCleanupHandler(host.FileStore, Options.Create(new PresentationWorkspaceOptions()), TimeProvider.System, NullLogger<PresentationWorkspaceCleanupHandler>.Instance);
        await handler.CleanupAsync(host.Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction, TestContext.Current.CancellationToken);

        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
    }

    /// <summary>
    /// Removing an upload removes the working copy made from it, and it is not imported again.
    /// </summary>
    [Fact]
    public async Task DocumentHandler_RemovesTheImportedDeck()
    {
        using var host = new PresentationToolTestHost();
        var package = await host.Engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var upload = await host.UploadAsync("deck.pptx", package);

        await host.InvokeAsync(PresentationToolNames.GetPresentationOutline, new { });
        Assert.Single((await host.LoadWorkspaceAsync()).State.Decks);

        var handler = new PresentationWorkspaceDocumentEventHandler(host.FileStore, host.Services.GetRequiredService<IOptions<PresentationWorkspaceOptions>>(), TimeProvider.System, NullLogger<PresentationWorkspaceDocumentEventHandler>.Instance);
        await handler.RemovedAsync(
            new AIChatDocumentRemoveContext
            {
                ReferenceId = host.Interaction.ItemId,
                ReferenceType = AIReferenceTypes.Document.ChatInteraction,
                DocumentInfo = new ChatDocumentInfo { DocumentId = upload.ItemId, FileName = upload.FileName },
            },
            TestContext.Current.CancellationToken);

        var workspace = await host.LoadWorkspaceAsync();

        Assert.Empty(workspace.State.Decks);
        Assert.Contains(upload.ItemId, workspace.State.DismissedDocumentIds);
    }

    /// <summary>
    /// The primary model is sent to the agent rather than writing a deck with generate_file.
    /// </summary>
    [Fact]
    public async Task GenerateFile_SendsDecksToTheAgent()
    {
        using var host = new PresentationToolTestHost(configure: services => services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>)));

        var response = await host.InvokeAsync(new GenerateFileTool(), new { content = "# Deck\n## Slide\n- Point", file_name = "deck.pptx" });

        Assert.Contains(PresentationAgentProvider.AgentName, response, StringComparison.Ordinal);
        Assert.DoesNotContain("[doc:", response, StringComparison.Ordinal);
    }
}
