using System.Text.Json.Nodes;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIPersonaCatalogTests
{
    [Fact]
    public void Parse_MapsFrontMatterAndBodyToPersona()
    {
        var parseResult = new TemplateParseResult
        {
            Body = "  You are Ava, a warm and patient assistant.  ",
            Metadata = new TemplateMetadata
            {
                Title = "Friendly assistant",
                Description = "Warm and plain-spoken.",
                AdditionalProperties =
                {
                    [nameof(AIPersona.AssistantName)] = "Ava",
                    [nameof(AIPersona.Language)] = "en-US",
                    [nameof(AIPersona.VoiceName)] = "marin",
                    [nameof(AIPersona.VoiceDeploymentName)] = "realtime",
                    [nameof(AIPersona.AvatarUrl)] = " /media/ava.png ",
                },
            },
        };

        var persona = AIPersonaFileParser.Parse("friendly-assistant", parseResult);

        Assert.Equal("friendly-assistant", persona.ItemId);
        Assert.Equal("friendly-assistant", persona.Name);
        Assert.Equal("Friendly assistant", persona.DisplayText);
        Assert.Equal("Warm and plain-spoken.", persona.Description);
        Assert.Equal("You are Ava, a warm and patient assistant.", persona.Instructions);
        Assert.Equal("Ava", persona.AssistantName);
        Assert.Equal("en-US", persona.Language);
        Assert.Equal("marin", persona.VoiceName);
        Assert.Equal("realtime", persona.VoiceDeploymentName);
        Assert.Equal("/media/ava.png", persona.AvatarUrl);
    }

    [Fact]
    public void Parse_WithoutTitle_DerivesTheDisplayNameFromTheId()
    {
        var persona = AIPersonaFileParser.Parse("patient-tutor", new TemplateParseResult { Body = "Teach slowly." });

        Assert.Equal("patient tutor", persona.DisplayText);
        Assert.Null(persona.AssistantName);
    }

    [Fact]
    public async Task FileSystemProvider_ReadsPersonaFilesUnderTheContentRoot()
    {
        var contentRoot = Directory.CreateTempSubdirectory("crestapps-personas-").FullName;

        try
        {
            var folder = Path.Combine(contentRoot, "Templates", "Personas", "Support");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "ava.md"), "---\nTitle: Ava\nAssistantName: Ava\n---\nYou are Ava.", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "Not a persona.", TestContext.Current.CancellationToken);

            var provider = new AIPersonaFileSystemProvider(
                new TestHostEnvironment(contentRoot),
                [new DefaultMarkdownTemplateParser()],
                NullLogger<AIPersonaFileSystemProvider>.Instance);

            var personas = await provider.GetPersonasAsync(TestContext.Current.CancellationToken);

            var persona = Assert.Single(personas);
            Assert.Equal("Support.ava", persona.Name);
            Assert.Equal("Ava", persona.AssistantName);
            Assert.Equal("You are Ava.", persona.Instructions);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Manager_GetAllAsync_SavedPersonaReplacesTheFileWithTheSameName()
    {
        var saved = new AIPersona { ItemId = "saved-1", Name = "Friendly", Instructions = "Saved." };
        var manager = CreateManager(
            [saved],
            [
                new AIPersona { ItemId = "friendly", Name = "friendly", Instructions = "From file." },
                new AIPersona { ItemId = "tutor", Name = "tutor", Instructions = "From file." },
            ]);

        var personas = (await manager.GetAllAsync(TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, personas.Count);
        Assert.Same(saved, personas.Single(persona => string.Equals(persona.Name, "friendly", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(personas, persona => persona.Name == "tutor");
    }

    [Fact]
    public async Task Manager_FindByNameAsync_FallsBackToProvidedPersonas()
    {
        var manager = CreateManager([], [new AIPersona { ItemId = "tutor", Name = "tutor", Instructions = "From file." }]);

        var persona = await manager.FindByNameAsync("Tutor", TestContext.Current.CancellationToken);

        Assert.NotNull(persona);
        Assert.Equal("tutor", persona.Name);
    }

    [Fact]
    public async Task Manager_FindByIdAsync_FallsBackToProvidedPersonas()
    {
        var manager = CreateManager([], [new AIPersona { ItemId = "tutor", Name = "tutor", Instructions = "From file." }]);

        Assert.NotNull(await manager.FindByIdAsync("tutor", TestContext.Current.CancellationToken));
        Assert.Null(await manager.FindByIdAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Manager_NewAsync_FillsThePersonaFromData()
    {
        var manager = CreateManager([], []);

        var persona = await manager.NewAsync("ava", new JsonObject
        {
            [nameof(AIPersona.DisplayText)] = " Ava ",
            [nameof(AIPersona.Instructions)] = "You are Ava.",
            [nameof(AIPersona.AssistantName)] = "Ava",
            [nameof(AIPersona.AvatarUrl)] = "",
        }, TestContext.Current.CancellationToken);

        Assert.Equal("ava", persona.Name);
        Assert.Equal("Ava", persona.DisplayText);
        Assert.Equal("You are Ava.", persona.Instructions);
        Assert.Equal("Ava", persona.AssistantName);
        Assert.Null(persona.AvatarUrl);
        Assert.NotEqual(default, persona.CreatedUtc);
    }

    [Fact]
    public async Task Manager_UpdateAsync_KeepsTheTechnicalName()
    {
        var persona = new AIPersona { ItemId = "saved-1", Name = "ava", Instructions = "Old." };
        var manager = CreateManager([persona], []);

        await manager.UpdateAsync(persona, new JsonObject
        {
            [nameof(AIPersona.Name)] = "renamed",
            [nameof(AIPersona.Instructions)] = "New.",
        }, TestContext.Current.CancellationToken);

        Assert.Equal("ava", persona.Name);
        Assert.Equal("New.", persona.Instructions);
        Assert.NotNull(persona.ModifiedUtc);
    }

    [Fact]
    public async Task Manager_ValidateAsync_RequiresNameAndInstructions()
    {
        var manager = CreateManager([], []);

        var result = await manager.ValidateAsync(new AIPersona(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(AIPersona.Name)));
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(AIPersona.Instructions)));
    }

    [Fact]
    public async Task Manager_ValidateAsync_RejectsADuplicateName()
    {
        var existing = new AIPersona { ItemId = "saved-1", Name = "ava", Instructions = "Saved." };
        var manager = CreateManager([existing], []);

        var result = await manager.ValidateAsync(new AIPersona { ItemId = "saved-2", Name = "ava", Instructions = "Another." }, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(AIPersona.Name)));
        Assert.True((await manager.ValidateAsync(existing, TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public void AddPersonas_RegistersThePersonaFeature()
    {
        var services = new ServiceCollection();

        new CrestAppsAISuiteBuilder(services).AddPersonas(personas => Assert.Same(services, personas.Services));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAIPersonaManager));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAIPersonaResolver));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAICompletionContextBuilderHandler) &&
            descriptor.ImplementationType == typeof(AIPersonaCompletionContextBuilderHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAIPersonaProvider) &&
            descriptor.ImplementationType == typeof(AIPersonaFileSystemProvider));
    }

    [Fact]
    public void AddCoreAIServices_WithoutPersonas_RegistersNoPersonaServices()
    {
        var services = new ServiceCollection();

        services.AddCoreAIServices();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAIPersonaManager));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAIPersonaResolver));
    }

    [Fact]
    public async Task ServiceProviderExtensions_WithoutPersonas_ResolveNoPersonaAndKeepTheVoices()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var profile = new AIProfile();
        profile.Put(new AIPersonaMetadata { PersonaId = "ava" });

        Assert.Null(await services.ResolvePersonaAsync(profile, TestContext.Current.CancellationToken));
        Assert.Equal("explicit", await services.SelectPersonaVoiceAsync(null, "explicit", "tts", "default", TestContext.Current.CancellationToken));
        Assert.Equal("default", await services.SelectPersonaVoiceAsync(null, null, "tts", "default", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServiceProviderExtensions_WithPersonas_UseTheResolver()
    {
        var persona = new ResolvedAIPersona { Id = "ava", VoiceName = "marin" };
        var profile = new AIProfile();
        var resolver = new Mock<IAIPersonaResolver>();
        resolver.Setup(r => r.ResolveAsync(profile, It.IsAny<CancellationToken>())).ReturnsAsync(persona);
        resolver.Setup(r => r.SelectVoiceAsync(persona, null, "tts", "default", It.IsAny<CancellationToken>())).ReturnsAsync("marin");
        var services = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();

        Assert.Same(persona, await services.ResolvePersonaAsync(profile, TestContext.Current.CancellationToken));
        Assert.Equal("marin", await services.SelectPersonaVoiceAsync(persona, null, "tts", "default", TestContext.Current.CancellationToken));
    }

    private static DefaultAIPersonaManager CreateManager(IEnumerable<AIPersona> saved, IEnumerable<AIPersona> provided)
    {
        var savedList = saved.ToList();
        var catalog = new Mock<INamedCatalog<AIPersona>>();
        catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => savedList);
        catalog
            .Setup(c => c.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => savedList.FirstOrDefault(persona => string.Equals(persona.Name, name, StringComparison.OrdinalIgnoreCase)));
        catalog
            .Setup(c => c.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => savedList.FirstOrDefault(persona => persona.ItemId == id));

        var provider = new Mock<IAIPersonaProvider>();
        provider
            .Setup(p => p.GetPersonasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(provided.ToList());

        var handler = new AIPersonaCatalogHandler(
            new HttpContextAccessor(),
            TimeProvider.System,
            catalog.Object,
            new PassThroughLocalizer());

        return new DefaultAIPersonaManager(
            catalog.Object,
            [handler],
            [provider.Object],
            NullLogger<DefaultAIPersonaManager>.Instance);
    }

    // The handler is internal, so its localizer cannot be proxied by a mocking library.
    private sealed class PassThroughLocalizer : IStringLocalizer<AIPersonaCatalogHandler>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CrestApps.Core.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
