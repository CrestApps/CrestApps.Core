using System.Text.Json.Nodes;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AISystemPromptTemplateSeederTests
{
    private static readonly System.Reflection.Assembly _coreAIAssembly = typeof(DefaultAISystemPromptTemplateSeeder).Assembly;

    [Theory]
    [InlineData(AISpeakingStyles.Category, "speaking-style-")]
    [InlineData(AIGuardrailTopics.Category, "guardrail-topic-")]
    public async Task Defaults_ShipEveryBuiltInTemplateInItsCategory(string category, string prefix)
    {
        var seeder = NewSeeder(new ServiceCollection().BuildServiceProvider(), new AISystemPromptTemplateDefaultsSource(_coreAIAssembly, category));
        var builtIn = category == AISpeakingStyles.Category
            ? AISpeakingStyles.BuiltIn
            : AIGuardrailTopics.BuiltIn;

        var defaults = await seeder.GetDefaultsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(8, builtIn.Count);
        Assert.Equal(builtIn.Order(StringComparer.Ordinal), defaults.Select(template => template.Id).Order(StringComparer.Ordinal));
        Assert.All(defaults, template =>
        {
            Assert.StartsWith(prefix, template.Id);
            Assert.Equal(category, template.Metadata.Category);
            Assert.False(template.Metadata.IsListable);
            Assert.False(string.IsNullOrWhiteSpace(template.Metadata.Title));
            Assert.False(string.IsNullOrWhiteSpace(template.Content));
            Assert.Equal(AITemplateSources.SystemPrompt, template.Kind);
        });
    }

    [Fact]
    public async Task Defaults_AreReadOnceEachAndFilteredByCategory()
    {
        var seeder = NewSeeder(
            new ServiceCollection().BuildServiceProvider(),
            new AISystemPromptTemplateDefaultsSource(_coreAIAssembly, AISpeakingStyles.Category),
            new AISystemPromptTemplateDefaultsSource(_coreAIAssembly, AIGuardrailTopics.Category),
            new AISystemPromptTemplateDefaultsSource(_coreAIAssembly));

        Assert.Equal(16, (await seeder.GetDefaultsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
        Assert.Equal(8, (await seeder.GetDefaultsAsync(AIGuardrailTopics.Category, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Seed_CreatesTheMissingTemplatesAsEditableSystemPrompts()
    {
        var created = new List<AIProfileTemplate>();
        var manager = new Mock<IAIProfileTemplateManager>();
        manager
            .Setup(value => value.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => name == AIGuardrailTopics.Hate ? new AIProfileTemplate { Name = name } : null);
        manager
            .Setup(value => value.NewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonNode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string source, JsonNode _, CancellationToken _) => new AIProfileTemplate { Name = name, Source = source });
        manager
            .Setup(value => value.CreateAsync(It.IsAny<AIProfileTemplate>(), It.IsAny<CancellationToken>()))
            .Callback((AIProfileTemplate template, CancellationToken _) => created.Add(template))
            .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(manager.Object)
            .AddSingleton(Mock.Of<INamedSourceCatalog<AIProfileTemplate>>())
            .BuildServiceProvider();
        var seeder = NewSeeder(services, new AISystemPromptTemplateDefaultsSource(_coreAIAssembly, AIGuardrailTopics.Category));

        var names = await seeder.SeedAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The template saved already is left as it is.
        Assert.Equal(7, names.Count);
        Assert.DoesNotContain(AIGuardrailTopics.Hate, names);

        var violence = Assert.Single(created, template => template.Name == AIGuardrailTopics.Violence);
        Assert.Equal(AITemplateSources.SystemPrompt, violence.Source);
        Assert.Equal("Violence and weapons", violence.DisplayText);
        Assert.Equal(AIGuardrailTopics.Category, violence.Category);
        Assert.False(violence.IsListable);
        Assert.True(violence.TryGet<SystemPromptTemplateMetadata>(out var metadata));
        Assert.Equal("Violence, graphic injury, or making or using weapons.", metadata.SystemMessage);
    }

    [Fact]
    public async Task Seed_DoesNothingWithoutATemplateStore()
    {
        var seeder = NewSeeder(new ServiceCollection().BuildServiceProvider(), new AISystemPromptTemplateDefaultsSource(_coreAIAssembly));

        Assert.Empty(await seeder.SeedAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private static DefaultAISystemPromptTemplateSeeder NewSeeder(IServiceProvider services, params AISystemPromptTemplateDefaultsSource[] sources)
    {
        var options = new AISystemPromptTemplateDefaultsOptions();

        foreach (var source in sources)
        {
            options.Sources.Add(source);
        }

        return new DefaultAISystemPromptTemplateSeeder(
            services,
            [new DefaultMarkdownTemplateParser()],
            Options.Create(options),
            NullLogger<DefaultAISystemPromptTemplateSeeder>.Instance);
    }
}
