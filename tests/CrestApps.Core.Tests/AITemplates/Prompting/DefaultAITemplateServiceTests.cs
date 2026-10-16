using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;
using CrestApps.Core.Templates.Rendering;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.AITemplates.Prompting;

public sealed class DefaultAITemplateServiceTests
{
    [Fact]
    public async Task ListAsync_ReturnsAllTemplatesFromProviders()
    {
        var provider1 = new InMemoryProvider(
        [
            new Template { Id = "p1", Content = "Prompt one" },
            ]);
        var provider2 = new InMemoryProvider(
        [
        new Template { Id = "p2", Content = "Prompt two" },
            ]);

        var service = CreateService([provider1, provider2]);

        var templates = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, t => t.Id == "p1");
        Assert.Contains(templates, t => t.Id == "p2");
    }

    [Fact]
    public async Task ListAsync_DuplicateIds_UsesFirstTemplate()
    {
        var provider1 = new InMemoryProvider(
        [
            new Template { Id = "shared", Content = "First" },
        ]);
        var provider2 = new InMemoryProvider(
        [
            new Template { Id = "shared", Content = "Second" },
        ]);

        var service = CreateService([provider1, provider2]);

        var templates = await service.ListAsync(TestContext.Current.CancellationToken);

        var template = Assert.Single(templates);
        Assert.Equal("shared", template.Id);
        Assert.Equal("First", template.Content);
    }

    [Fact]
    public async Task ListAsync_NoProviders_ReturnsEmpty()
    {
        var service = CreateService([]);

        var templates = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(templates);
    }

    [Fact]
    public async Task ListByKindAsync_ReturnsTemplatesMatchingKind()
    {
        var provider = new InMemoryProvider(
        [
            new Template { Id = "system-1", Kind = "SystemPrompt", Content = "System prompt" },
            new Template { Id = "profile-1", Kind = "Profile", Content = "Profile template" },
            new Template { Id = "system-2", Kind = "systemprompt", Content = "Another system prompt" },
        ]);

        var service = CreateService([provider]);

        var templates = await service.GetByKindAsync("SystemPrompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, template => template.Id == "system-1");
        Assert.Contains(templates, template => template.Id == "system-2");
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsTemplate()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "test-prompt", Content = "Hello world" },
            ]);

        var service = CreateService([provider]);

        var template = await service.GetAsync("test-prompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(template);
        Assert.Equal("Hello world", template.Content);
    }

    [Fact]
    public async Task GetAsync_CaseInsensitive_ReturnsTemplate()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "Test-Prompt", Content = "Hello" },
            ]);

        var service = CreateService([provider]);

        var template = await service.GetAsync("test-prompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(template);
    }

    [Fact]
    public async Task GetAsync_NonExistentId_ReturnsNull()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "existing", Content = "Hello" },
            ]);

        var service = CreateService([provider]);

        var template = await service.GetAsync("non-existent", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(template);
    }

    [Fact]
    public async Task RenderAsync_ExistingTemplate_ReturnsRenderedContent()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "greeting", Content = "Hello, {{ name }}!" },
            ]);

        var service = CreateService([provider]);

        var result = await service.RenderAsync("greeting", new Dictionary<string, object>
        {
            ["name"] = "World",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task RenderAsync_PlainTemplate_ReturnsPlainText()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "simple", Content = "You are an AI." },
            ]);

        var service = CreateService([provider]);

        var result = await service.RenderAsync("simple", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("You are an AI.", result);
    }

    [Fact]
    public async Task RenderAsync_NonExistentTemplate_ThrowsKeyNotFoundException()
    {
        var service = CreateService([]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RenderAsync("missing", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MergeAsync_MultipleTemplates_ConcatenatesRendered()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "a", Content = "Part A" },
            new Template { Id = "b", Content = "Part B" },
            ]);

        var service = CreateService([provider]);

        var result = await service.MergeAsync(["a", "b"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Part A\n\nPart B", result);
    }

    [Fact]
    public async Task MergeAsync_CustomSeparator_UsesIt()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "x", Content = "X" },
            new Template { Id = "y", Content = "Y" },
            ]);

        var service = CreateService([provider]);

        var result = await service.MergeAsync(["x", "y"], separator: " | ", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("X | Y", result);
    }

    [Fact]
    public async Task MergeAsync_ThrowsOnNonExistentTemplate()
    {
        var provider = new InMemoryProvider(
        [
        new Template { Id = "a", Content = "Part A" },
            ]);

        var service = CreateService([provider]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MergeAsync(["a", "missing"], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MergeAsync_AllMissing_ThrowsKeyNotFoundException()
    {
        var service = CreateService([]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MergeAsync(["missing1", "missing2"], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListAsync_DynamicProviderRegisteredFirst_UsesStaticTemplateForSharedId()
    {
        var dynamicProvider = new DynamicInMemoryProvider(
        [
            new Template { Id = "shared", Content = "Dynamic" },
            new Template { Id = "dynamic-only", Content = "Dynamic only" },
        ]);
        var staticProvider = new InMemoryProvider(
        [
            new Template { Id = "shared", Content = "Static" },
        ]);

        var service = CreateService([dynamicProvider, staticProvider]);

        var templates = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["shared", "dynamic-only"], templates.Select(template => template.Id));
        Assert.Equal("Static", templates[0].Content);
    }

    [Fact]
    public async Task GetAsync_IdMatchesAlias_ReturnsTemplate()
    {
        var provider = new InMemoryProvider(
        [
            new Template { Id = "current-id", Aliases = ["earlier-id"], Content = "Aliased" },
        ]);

        var service = CreateService([provider]);

        var template = await service.GetAsync("EARLIER-ID", TestContext.Current.CancellationToken);

        Assert.NotNull(template);
        Assert.Equal("current-id", template.Id);
    }

    [Fact]
    public async Task GetAsync_IdMatchesOneTemplateAndAliasOfAnother_ReturnsIdMatch()
    {
        var provider = new InMemoryProvider(
        [
            new Template { Id = "first", Aliases = ["second"], Content = "Alias owner" },
            new Template { Id = "second", Content = "Id owner" },
        ]);

        var service = CreateService([provider]);

        var template = await service.GetAsync("second", TestContext.Current.CancellationToken);

        Assert.Equal("Id owner", template.Content);
    }

    [Fact]
    public async Task RenderAsync_IdMatchesAlias_RendersTemplate()
    {
        var provider = new DynamicInMemoryProvider(
        [
            new Template { Id = "current-id", Aliases = ["earlier-id"], Content = "Hello {{ name }}" },
        ]);

        var service = CreateService([provider]);

        var rendered = await service.RenderAsync("earlier-id", new Dictionary<string, object> { ["name"] = "World" }, TestContext.Current.CancellationToken);

        Assert.Equal("Hello World", rendered);
    }

    [Fact]
    public async Task ListAsync_StaticTemplatesOverridden_ReadsDynamicProvidersOnEveryInstance()
    {
        var staticProvider = new CountingProvider(
        [
            new Template { Id = "static", Content = "Static" },
        ]);
        var dynamicProvider = new CountingDynamicProvider(
        [
            new Template { Id = "dynamic", Content = "Dynamic" },
        ]);
        var staticCache = new StaticTemplateCache();

        var first = await CreateCachingService([staticProvider, dynamicProvider], staticCache).ListAsync(TestContext.Current.CancellationToken);
        var second = await CreateCachingService([staticProvider, dynamicProvider], staticCache).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["static", "dynamic"], first.Select(template => template.Id));
        Assert.Equal(["static", "dynamic"], second.Select(template => template.Id));
        Assert.Equal(1, staticProvider.Calls);
        Assert.Equal(2, dynamicProvider.Calls);
    }

    private static DefaultTemplateService CreateService(ITemplateProvider[] providers)
    {
        return new DefaultTemplateService(providers, CreateRenderer());
    }

    private static CachingTemplateService CreateCachingService(ITemplateProvider[] providers, StaticTemplateCache staticCache)
    {
        return new CachingTemplateService(providers, CreateRenderer(), staticCache);
    }

    private static FluidTemplateEngine CreateRenderer()
    {
        var sp = new ServiceCollection().BuildServiceProvider();

        return new FluidTemplateEngine(
        sp,
        Microsoft.Extensions.Options.Options.Create(new Fluid.TemplateOptions()),
        NullLogger<FluidTemplateEngine>.Instance);
    }

    private sealed class StaticTemplateCache
    {
        public IReadOnlyList<Template> Templates { get; set; }
    }

    private sealed class CachingTemplateService : DefaultTemplateService
    {
        private readonly StaticTemplateCache _staticCache;

        public CachingTemplateService(
            IEnumerable<ITemplateProvider> providers,
            ITemplateEngine renderer,
            StaticTemplateCache staticCache)
            : base(providers, renderer)
        {
            _staticCache = staticCache;
        }

        protected override async Task<IReadOnlyList<Template>> GetStaticTemplatesAsync(CancellationToken cancellationToken = default)
        {
            return _staticCache.Templates ??= await base.GetStaticTemplatesAsync(cancellationToken);
        }
    }

    private sealed class DynamicInMemoryProvider : IDynamicTemplateProvider
    {
        private readonly IReadOnlyList<Template> _templates;

        public DynamicInMemoryProvider(Template[] templates)
        {
            _templates = templates;
        }

        public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_templates);
        }
    }

    private class CountingProvider : ITemplateProvider
    {
        private readonly IReadOnlyList<Template> _templates;

        public CountingProvider(Template[] templates)
        {
            _templates = templates;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(_templates);
        }
    }

    private sealed class CountingDynamicProvider : CountingProvider, IDynamicTemplateProvider
    {
        public CountingDynamicProvider(Template[] templates)
            : base(templates)
        {
        }
    }

    private sealed class InMemoryProvider : ITemplateProvider
    {
        private readonly IReadOnlyList<Template> _templates;

        public InMemoryProvider(Template[] templates)
        {
            _templates = templates;
        }

        public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_templates);
        }
    }
}
