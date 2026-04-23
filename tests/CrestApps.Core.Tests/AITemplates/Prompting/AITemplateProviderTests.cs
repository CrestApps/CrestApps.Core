using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Startup.Shared.Services;
using CrestApps.Core.Templates;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Providers;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.AITemplates.Prompting;

public sealed class OptionsAITemplateProviderTests
{
    [Fact]
    public async Task GetTemplatesAsync_ReturnsRegisteredTemplates()
    {
        var options = new TemplateOptions();
        options.Templates.Add(new Template { Id = "code-template", Content = "Registered via code." });
        options.Templates.Add(new Template { Id = "another", Content = "Another one." });

        var provider = new OptionsTemplateProvider(Options.Create(options));

        var result = await provider.GetTemplatesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == "code-template");
        Assert.Contains(result, t => t.Id == "another");
    }

    [Fact]
    public async Task GetTemplatesAsync_EmptyOptions_ReturnsEmpty()
    {
        var options = new TemplateOptions();
        var provider = new OptionsTemplateProvider(Options.Create(options));

        var result = await provider.GetTemplatesAsync();

        Assert.Empty(result);
    }
}

public sealed class PromptsFileSystemAITemplateProviderTests : IDisposable
{
    private readonly string _tempDir;

    public PromptsFileSystemAITemplateProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CrestAppsPromptTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_DiscoversMdFiles()
    {
        var promptsDir = Path.Combine(_tempDir, "Templates", "Prompts");
        Directory.CreateDirectory(promptsDir);

        File.WriteAllText(Path.Combine(promptsDir, "test-prompt.md"), """
            ---
            Title: Test Prompt
            Description: A test prompt
            ---
            You are a test assistant.
            """);

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new PromptsFileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<PromptsFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        Assert.Single(templates);
        Assert.Equal("test-prompt", templates[0].Id);
        Assert.Equal(AITemplateSources.SystemPrompt, templates[0].Kind);
        Assert.Equal("Test Prompt", templates[0].Metadata.Title);
        Assert.Contains("You are a test assistant.", templates[0].Content);
    }

    [Fact]
    public async Task GetTemplatesAsync_DiscoverFeatureSubdirectories()
    {
        var promptsDir = Path.Combine(_tempDir, "Templates", "Prompts");
        var featureDir = Path.Combine(promptsDir, "MyModule.Feature");
        Directory.CreateDirectory(featureDir);

        File.WriteAllText(Path.Combine(featureDir, "feature-prompt.md"), """
            ---
            Title: Feature Prompt
            ---
            Feature-specific content.
            """);

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new PromptsFileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<PromptsFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        Assert.Single(templates);
        Assert.Equal("feature-prompt", templates[0].Id);
        Assert.Equal(AITemplateSources.SystemPrompt, templates[0].Kind);
        Assert.Equal("MyModule.Feature", templates[0].FeatureId);
    }

    [Fact]
    public async Task GetTemplatesAsync_NoPromptsDirectory_ReturnsEmpty()
    {
        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new PromptsFileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<PromptsFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        Assert.Empty(templates);
    }

    [Fact]
    public async Task GetTemplatesAsync_UsesFilenameAsTitleWhenNotInFrontMatter()
    {
        var promptsDir = Path.Combine(_tempDir, "Templates", "Prompts");
        Directory.CreateDirectory(promptsDir);

        File.WriteAllText(Path.Combine(promptsDir, "my-cool-prompt.md"), "Just body, no front matter.");

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new PromptsFileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<PromptsFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        Assert.Single(templates);
        Assert.Equal("my cool prompt", templates[0].Metadata.Title);
    }

    [Fact]
    public async Task GetTemplatesAsync_IgnoresNonMdFiles()
    {
        var promptsDir = Path.Combine(_tempDir, "Templates", "Prompts");
        Directory.CreateDirectory(promptsDir);

        File.WriteAllText(Path.Combine(promptsDir, "valid.md"), "Valid prompt.");
        File.WriteAllText(Path.Combine(promptsDir, "readme.txt"), "Not a prompt.");
        File.WriteAllText(Path.Combine(promptsDir, "data.json"), "{}");

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new PromptsFileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<PromptsFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        Assert.Single(templates);
        Assert.Equal("valid", templates[0].Id);
    }
}

public sealed class FileSystemAITemplateProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemAITemplateProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CrestAppsGenericTemplateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_DiscoversTemplatesFromTemplatesDirectory()
    {
        var templatesDir = Path.Combine(_tempDir, "Templates");
        Directory.CreateDirectory(templatesDir);

        File.WriteAllText(Path.Combine(templatesDir, "generic-template.md"), """
            ---
            Title: Generic Template
            Kind: Profile
            Category: General
            ---
            Generic template content.
            """);

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new FileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<FileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("generic-template", template.Id);
        Assert.Equal(AITemplateSources.Profile, template.Kind);
        Assert.Equal("Generic Template", template.Metadata.Title);
    }

    [Fact]
    public async Task GetTemplatesAsync_IgnoresTemplateSubdirectories()
    {
        var templatesDir = Path.Combine(_tempDir, "Templates");
        var nestedDir = Path.Combine(templatesDir, "Sources");
        Directory.CreateDirectory(nestedDir);

        File.WriteAllText(Path.Combine(templatesDir, "test.md"), """
            ---
            Title: Root Template
            Kind: Profile
            ---
            Root content.
            """);

        File.WriteAllText(Path.Combine(nestedDir, "test.md"), """
            ---
            Title: Nested Template
            Kind: Profile
            ---
            Nested content.
            """);

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDir);

        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var provider = new FileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<FileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("test", template.Id);
        Assert.Equal("Root Template", template.Metadata.Title);
    }
}

public sealed class EmbeddedResourceAITemplateProviderTests
{
    [Fact]
    public async Task GetTemplatesAsync_DiscoversEmbeddedResources()
    {
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };

        // Use the test assembly which has embedded Templates/Prompts/*.md files.
        var assembly = typeof(EmbeddedResourceAITemplateProviderTests).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        Assert.NotEmpty(templates);
        Assert.Contains(templates, t => t.Id == "test-template");
        Assert.Contains(templates, t => t.Id == "generic-template");
    }

    [Fact]
    public async Task GetTemplatesAsync_ParsesFrontMatter()
    {
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };

        var assembly = typeof(EmbeddedResourceAITemplateProviderTests).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        var testTemplate = templates.FirstOrDefault(t => t.Id == "test-template");
        Assert.NotNull(testTemplate);
        Assert.Equal("Test Template", testTemplate.Metadata.Title);
        Assert.Equal(AITemplateSources.SystemPrompt, testTemplate.Kind);
        Assert.True(testTemplate.Metadata.IsListable);
        Assert.Equal("Testing", testTemplate.Metadata.Category);
        Assert.NotEmpty(testTemplate.Content);
    }

    [Fact]
    public async Task GetTemplatesAsync_UsesKindMetadataForGenericEmbeddedTemplates()
    {
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };

        var assembly = typeof(EmbeddedResourceAITemplateProviderTests).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates, t => t.Id == "generic-template");
        Assert.Equal(AITemplateSources.Profile, template.Kind);
        Assert.Equal("Generic Template", template.Metadata.Title);
    }

    [Fact]
    public async Task GetTemplatesAsync_SetsSourceFromAssemblyName()
    {
        var options = new TemplateOptions();
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };

        var assembly = typeof(EmbeddedResourceAITemplateProviderTests).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        Assert.All(templates, t => Assert.Equal(assembly.GetName().Name, t.Source));
    }

    [Fact]
    public async Task GetTemplatesAsync_CustomSourceAndFeatureId()
    {
        var options = new TemplateOptions();
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };

        var assembly = typeof(EmbeddedResourceAITemplateProviderTests).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers, source: "MySource", featureId: "MyFeature");

        var templates = await provider.GetTemplatesAsync();

        Assert.All(templates, t =>
        {
            Assert.Equal("MySource", t.Source);
            Assert.Equal("MyFeature", t.FeatureId);
        });
    }

    [Fact]
    public async Task GetTemplatesAsync_FrameworkAssemblyIncludesDocumentContextHeader()
    {
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var assembly = typeof(AI.AITemplateIds).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates, t => t.Id == CrestApps.Core.AI.AITemplateIds.DocumentContextHeader);
        Assert.Equal("Document Context Header", template.Metadata.Title);
        Assert.Contains("[Uploaded Document Context]", template.Content);
    }

    [Fact]
    public async Task GetTemplatesAsync_FrameworkAssemblyIncludesExtractedDataAvailability()
    {
        var parsers = new ITemplateParser[] { new DefaultMarkdownTemplateParser() };
        var assembly = typeof(AI.AITemplateIds).Assembly;
        var provider = new EmbeddedResourceTemplateProvider(assembly, parsers);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates, t => t.Id == CrestApps.Core.AI.AITemplateIds.ExtractedDataAvailability);
        Assert.Equal("Extracted Data Availability", template.Metadata.Title);
        Assert.Contains("[Collected Session Data]", template.Content);
    }
}

public sealed class FileSystemAIProfileTemplateProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemAIProfileTemplateProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CrestAppsProfileTemplateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_DiscoversSystemPromptTemplatesFromProfilesFolder()
    {
        var profilesDir = Path.Combine(_tempDir, "Templates", "Profiles");
        Directory.CreateDirectory(profilesDir);

        File.WriteAllText(Path.Combine(profilesDir, "support-agent.md"), """
            ---
            Title: Support Agent
            Source: SystemPrompt
            IsListable: true
            Category: Support
            ---
            You are a helpful support assistant.
            """);

        var provider = new AIProfileFileSystemTemplateProvider(
            new TestHostEnvironment(_tempDir),
            [new DefaultMarkdownTemplateParser()],
            NullLogger<AIProfileFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("support-agent", template.Name);
        Assert.Equal("Support Agent", template.DisplayText);
        Assert.Equal("SystemPrompt", template.Source);
        Assert.True(template.IsListable);
        Assert.True(template.TryGet<CrestApps.Core.AI.Models.SystemPromptTemplateMetadata>(out var metadata));
        Assert.Equal("You are a helpful support assistant.", metadata.SystemMessage);
    }

    [Fact]
    public async Task GetTemplatesAsync_UsesRelativePathForNestedTemplateIds()
    {
        var profilesDir = Path.Combine(_tempDir, "Templates", "Profiles", "Nested");
        Directory.CreateDirectory(profilesDir);

        File.WriteAllText(Path.Combine(profilesDir, "system-prompt.md"), """
            ---
            Source: SystemPrompt
            ---
            Nested prompt.
            """);

        var provider = new AIProfileFileSystemTemplateProvider(
            new TestHostEnvironment(_tempDir),
            [new DefaultMarkdownTemplateParser()],
            NullLogger<AIProfileFileSystemTemplateProvider>.Instance);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("Nested.system-prompt", template.Name);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CrestApps.Core.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class RuntimeEmbeddedPromptRegistrationTests
{
    [Fact]
    public async Task TemplateService_ListsEmbeddedPromptTemplatesFromAiAndChatAssemblies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIOrchestration();
        services.AddCoreAIChatInteractions();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>();

        var templates = await templateService.ListAsync();

        Assert.Contains(templates, template => string.Equals(template.Id, "use-markdown-syntax", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(templates, template => string.Equals(template.Id, "tabular-batch-processing", StringComparison.OrdinalIgnoreCase));

        var markdownTemplate = Assert.Single(templates, template => string.Equals(template.Id, "use-markdown-syntax", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Use Markdown Syntax", markdownTemplate.Metadata.Title);
        Assert.True(markdownTemplate.Metadata.IsListable);
    }
}

public sealed class AIProfileSystemPromptTemplateProviderTests
{
    [Fact]
    public async Task GetTemplatesAsync_ExposesListableSystemPromptTemplatesFromTemplateManager()
    {
        var dbTemplate = new CrestApps.Core.AI.Models.AIProfileTemplate
        {
            ItemId = "db-template-1",
            Name = "DbTemplate",
            DisplayText = "DB Template",
            Description = "Stored in the catalog",
            Category = "Database",
            IsListable = true,
            Source = CrestApps.Core.AI.AITemplateSources.SystemPrompt,
        };
        dbTemplate.Put(new CrestApps.Core.AI.Models.SystemPromptTemplateMetadata
        {
            SystemMessage = "You are a database-backed template.",
        });

        var templateManager = new Mock<CrestApps.Core.AI.Profiles.IAIProfileTemplateManager>();
        templateManager.Setup(x => x.GetAllAsync()).ReturnsAsync([dbTemplate]);

        var provider = new AIProfileSystemPromptTemplateProvider(templateManager.Object);

        var templates = await provider.GetTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("ai-profile:db-template-1", template.Id);
        Assert.Equal(CrestApps.Core.AI.AITemplateSources.SystemPrompt, template.Kind);
        Assert.Equal("AIProfileTemplate", template.Source);
        Assert.Equal("DB Template", template.Metadata.Title);
        Assert.Equal("Stored in the catalog", template.Metadata.Description);
        Assert.Equal("Database", template.Metadata.Category);
        Assert.True(template.Metadata.IsListable);
        Assert.Equal("You are a database-backed template.", template.Content);
    }
}
