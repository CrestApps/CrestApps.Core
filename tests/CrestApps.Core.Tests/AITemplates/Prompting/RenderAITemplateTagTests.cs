using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Rendering;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.AITemplates.Prompting;

public sealed class RenderAITemplateTagTests
{
    [Fact]
    public async Task RenderAITemplateTag_RendersSubTemplateInline()
    {
        var engine = CreateEngine(new Template { Id = "greeting", Content = "Hello World" });
        var result = await engine.RenderAsync("""Before {% render_ai_template "greeting" %} After""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Before Hello World After", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_InheritsParentScopeVariables()
    {
        var engine = CreateEngine(new Template { Id = "sub", Content = "{{ name }} is {{ age }}" });
        var result = await engine.RenderAsync("""{% render_ai_template "sub" %}""", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30, }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Alice is 30", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_AssignedVariableAvailableInSubTemplate()
    {
        var engine = CreateEngine(new Template { Id = "sub", Content = "{{ greeting }} World" });
        var result = await engine.RenderAsync("""{% assign greeting = "Hello" %}{% render_ai_template "sub" %}""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Hello World", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_SubTemplateDoesNotLeakVariables()
    {
        var engine = CreateEngine(new Template { Id = "sub", Content = "{% assign leaked = \"secret\" %}Sub" });
        var result = await engine.RenderAsync("""{% render_ai_template "sub" %}|{{ leaked }}""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Sub", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_NonExistentTemplate_RendersNothing()
    {
        var engine = CreateEngine();
        var result = await engine.RenderAsync("""Before{% render_ai_template "missing" %}After""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("BeforeAfter", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_NestedRenderCalls_Work()
    {
        var engine = CreateEngine(new Template { Id = "outer", Content = """Inner: {% render_ai_template "inner" %}""" }, new Template { Id = "inner", Content = "Deep" });
        var result = await engine.RenderAsync("""{% render_ai_template "outer" %}""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Inner: Deep", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_WithWhereFilter_InheritsFilteredList()
    {
        var engine = CreateEngine(new Template { Id = "agents", Content = "{% for agent in agents %}- {{ agent.Name }}\n{% endfor %}", });
        var tools = new[]
        {
            new TestTool("Tool1", "A tool", "Local"),
            new TestTool("ResearchAgent", "Research", "Agent"),
            new TestTool("Tool2", "Another", "System"),
            new TestTool("PlannerAgent", "Planning", "Agent"),
        };
        var result = await engine.RenderAsync("""{% assign agents = tools | where: "Source", "Agent" %}{% render_ai_template "agents" %}""", new Dictionary<string, object> { ["tools"] = tools, }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("ResearchAgent", result);
        Assert.Contains("PlannerAgent", result);
        Assert.DoesNotContain("Tool1", result);
        Assert.DoesNotContain("Tool2", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_DynamicTemplateId()
    {
        var engine = CreateEngine(new Template { Id = "dynamic", Content = "Dynamic Content" });
        var result = await engine.RenderAsync("""{% assign tmpl = "dynamic" %}{% render_ai_template tmpl %}""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Dynamic Content", result);
    }

    [Fact]
    public async Task RenderAITemplateTag_EmptyTemplateId_RendersNothing()
    {
        var engine = CreateEngine();
        var result = await engine.RenderAsync("""Before{% render_ai_template "" %}After""", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("BeforeAfter", result);
    }

    private static FluidTemplateEngine CreateEngine(params Template[] subTemplates)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITemplateService>(new InMemoryTemplateService(subTemplates));
        var sp = services.BuildServiceProvider();
        return new FluidTemplateEngine(sp, Microsoft.Extensions.Options.Options.Create(new Fluid.TemplateOptions { MemberAccessStrategy = new Fluid.UnsafeMemberAccessStrategy() }), NullLogger<FluidTemplateEngine>.Instance);
    }

    private sealed class InMemoryTemplateService : ITemplateService
    {
        private readonly IReadOnlyList<Template> _templates;
        public InMemoryTemplateService(Template[] templates)
        {
            _templates = templates;
        }

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_templates);
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class TestTool
    {
        public TestTool(string name, string description, string source)
        {
            Name = name;
            Description = description;
            Source = source;
        }

        public string Name { get; }
        public string Description { get; }
        public string Source { get; }
    }
}
