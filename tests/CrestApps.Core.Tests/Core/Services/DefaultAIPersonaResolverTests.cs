using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultAIPersonaResolverTests
{
    [Fact]
    public async Task ResolveAsync_ResourceWithoutPersona_ReturnsNull()
    {
        var resolver = CreateResolver([]);

        var persona = await resolver.ResolveAsync(new AIProfile(), TestContext.Current.CancellationToken);

        Assert.Null(persona);
    }

    [Fact]
    public async Task ResolveAsync_LinkedPersona_RendersInstructionsWithParameters()
    {
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync("You work for {{ company }}.", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IDictionary<string, object> args, CancellationToken _) => $"You work for {args["company"]}.");

        var resolver = CreateResolver(
            [CreatePersona("friendly", "You work for {{ company }}.", voiceName: "marin")],
            engine: engine.Object);

        var profile = new AIProfile();
        profile.Put(new AIPersonaMetadata
        {
            PersonaId = "Friendly",
            Parameters = new Dictionary<string, object> { ["company"] = "Contoso" },
        });

        var persona = await resolver.ResolveAsync(profile, TestContext.Current.CancellationToken);

        Assert.NotNull(persona);
        Assert.Equal("friendly", persona.Id);
        Assert.Equal("You work for Contoso.", persona.Instructions);
        Assert.Equal("marin", persona.VoiceName);
    }

    [Fact]
    public async Task ResolveAsync_MissingPersona_ReturnsNullWithoutThrowing()
    {
        var resolver = CreateResolver([CreatePersona("friendly", "Hi")]);
        var profile = new AIProfile();
        profile.Put(new AIPersonaMetadata { PersonaId = "deleted-persona" });

        var persona = await resolver.ResolveAsync(profile, TestContext.Current.CancellationToken);

        Assert.Null(persona);
    }

    [Fact]
    public async Task ResolveAsync_BrokenLiquid_KeepsPersonaWithoutInstructions()
    {
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Liquid parse error"));

        var resolver = CreateResolver([CreatePersona("friendly", "{{ broken", voiceName: "marin")], engine: engine.Object);

        var persona = await resolver.ResolveAsync("friendly", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(persona);
        Assert.Null(persona.Instructions);
        Assert.Equal("marin", persona.VoiceName);
    }

    [Fact]
    public async Task SelectVoiceAsync_ExplicitVoice_AlwaysWins()
    {
        var resolver = CreateResolver([]);

        var voice = await resolver.SelectVoiceAsync(
            new ResolvedAIPersona { VoiceName = "marin" },
            "cedar",
            "realtime",
            "alloy",
            TestContext.Current.CancellationToken);

        Assert.Equal("cedar", voice);
    }

    [Fact]
    public async Task SelectVoiceAsync_PersonaVoiceWithoutDeploymentHint_IsUsed()
    {
        var resolver = CreateResolver([]);

        var voice = await resolver.SelectVoiceAsync(
            new ResolvedAIPersona { VoiceName = "marin" },
            null,
            "realtime",
            "alloy",
            TestContext.Current.CancellationToken);

        Assert.Equal("marin", voice);
    }

    [Fact]
    public async Task SelectVoiceAsync_PersonaVoiceFromSameProvider_IsUsed()
    {
        var resolver = CreateResolver(
            [],
            deployments:
            [
                new AIDeployment { Name = "realtime-a", ClientName = "OpenAI" },
                new AIDeployment { Name = "realtime-b", ClientName = "OpenAI" },
            ]);

        var voice = await resolver.SelectVoiceAsync(
            new ResolvedAIPersona { VoiceName = "marin", VoiceDeploymentName = "realtime-a" },
            null,
            "realtime-b",
            "alloy",
            TestContext.Current.CancellationToken);

        Assert.Equal("marin", voice);
    }

    [Fact]
    public async Task SelectVoiceAsync_PersonaVoiceFromOtherProvider_FallsBackToDefault()
    {
        var resolver = CreateResolver(
            [],
            deployments:
            [
                new AIDeployment { Name = "openai-realtime", ClientName = "OpenAI" },
                new AIDeployment { Name = "azure-speech", ClientName = "AzureSpeech" },
            ]);

        var voice = await resolver.SelectVoiceAsync(
            new ResolvedAIPersona { VoiceName = "marin", VoiceDeploymentName = "openai-realtime" },
            null,
            "azure-speech",
            "en-US-AvaNeural",
            TestContext.Current.CancellationToken);

        Assert.Equal("en-US-AvaNeural", voice);
    }

    [Fact]
    public async Task SelectVoiceAsync_CascadedDeployment_ComparesTheSpeakingProvider()
    {
        var cascaded = new AIDeployment { Name = "cascaded", ClientName = "OpenAI" };
        cascaded.Put(new CascadedRealtimeMetadata
        {
            SpeechToTextDeploymentName = "azure-stt",
            ChatDeploymentName = "chat",
            TextToSpeechDeploymentName = "azure-tts",
        });

        var resolver = CreateResolver(
            [],
            deployments:
            [
                cascaded,
                new AIDeployment { Name = "azure-tts", ClientName = "AzureSpeech" },
                new AIDeployment { Name = "azure-tts-2", ClientName = "AzureSpeech" },
            ]);

        var voice = await resolver.SelectVoiceAsync(
            new ResolvedAIPersona { VoiceName = "en-US-AvaNeural", VoiceDeploymentName = "azure-tts-2" },
            null,
            "cascaded",
            "alloy",
            TestContext.Current.CancellationToken);

        Assert.Equal("en-US-AvaNeural", voice);
    }

    [Fact]
    public async Task SelectVoiceAsync_NoVoiceAnywhere_ReturnsNull()
    {
        var resolver = CreateResolver([]);

        var voice = await resolver.SelectVoiceAsync(null, null, "realtime", null, TestContext.Current.CancellationToken);

        Assert.Null(voice);
    }

    [Fact]
    public async Task ResolveAsync_PersonaWithAssistantName_OffersItToTheInstructions()
    {
        IDictionary<string, object> renderedWith = null;
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, IDictionary<string, object> args, CancellationToken _) => renderedWith = args)
            .ReturnsAsync("I am Ava.");

        var persona = CreatePersona("friendly", "I am {{ assistant_name }}.");
        persona.AssistantName = " Ava ";
        var resolver = CreateResolver([persona], engine: engine.Object);

        var resolved = await resolver.ResolveAsync("friendly", new Dictionary<string, object> { ["company"] = "Contoso" }, TestContext.Current.CancellationToken);

        Assert.Equal("I am Ava.", resolved.Instructions);
        Assert.Equal("Ava", renderedWith[DefaultAIPersonaResolver.AssistantNameArgument]);
        Assert.Equal("Contoso", renderedWith["company"]);
    }

    [Fact]
    public async Task ResolveAsync_LinkArgumentNamedAssistantName_WinsOverThePersonaName()
    {
        IDictionary<string, object> renderedWith = null;
        var engine = new Mock<ITemplateEngine>();
        engine
            .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, IDictionary<string, object> args, CancellationToken _) => renderedWith = args)
            .ReturnsAsync("Rendered.");

        var persona = CreatePersona("friendly", "I am {{ assistant_name }}.");
        persona.AssistantName = "Ava";
        var resolver = CreateResolver([persona], engine: engine.Object);

        await resolver.ResolveAsync("friendly", new Dictionary<string, object> { ["assistant_name"] = "Max" }, TestContext.Current.CancellationToken);

        Assert.Equal("Max", renderedWith[DefaultAIPersonaResolver.AssistantNameArgument]);
    }

    private static AIPersona CreatePersona(string name, string instructions, string voiceName = null)
    {
        return new AIPersona
        {
            ItemId = name,
            Name = name,
            Instructions = instructions,
            VoiceName = voiceName,
        };
    }

    private static DefaultAIPersonaResolver CreateResolver(
        IEnumerable<AIPersona> personas,
        ITemplateEngine engine = null,
        IEnumerable<AIDeployment> deployments = null)
    {
        var personaManager = new Mock<IAIPersonaManager>();
        personaManager
            .Setup(manager => manager.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => personas.FirstOrDefault(persona => string.Equals(persona.Name, name, StringComparison.OrdinalIgnoreCase)));

        if (engine is null)
        {
            var passThrough = new Mock<ITemplateEngine>();
            passThrough
                .Setup(e => e.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string template, IDictionary<string, object> _, CancellationToken _) => template);
            engine = passThrough.Object;
        }

        var deploymentStore = new Mock<IAIDeploymentStore>();

        foreach (var deployment in deployments ?? [])
        {
            deploymentStore
                .Setup(store => store.FindByNameAsync(deployment.Name, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deployment);
        }

        return new DefaultAIPersonaResolver(
            personaManager.Object,
            engine,
            deploymentStore.Object,
            NullLogger<DefaultAIPersonaResolver>.Instance);
    }
}
