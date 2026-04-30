using CrestApps.Core.Templates.Extensions;
using CrestApps.Core.Templates.Services;
using CrestApps.Core.Templates.Tags;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.AITemplates.Prompting;

public sealed class IncludeTemplateFilterTests
{
    [Fact]
    public async Task IncludePrompt_HappyPath_RendersIncludedTemplate()
    {
        var service = new InMemoryTemplateService(new Dictionary<string, string>
        {
            ["greeting"] = "Hello World",
        });

        var context = CreateContext(service);

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("greeting"),
            new FilterArguments(),
            context);

        Assert.Equal("Hello World", result.ToStringValue());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IncludePrompt_BlankPromptId_ReturnsNil(string promptId)
    {
        var service = new InMemoryTemplateService(new Dictionary<string, string>());
        var context = CreateContext(service);

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            promptId is null ? NilValue.Instance : new StringValue(promptId),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);
    }

    [Fact]
    public async Task IncludePrompt_MissingTemplate_ReturnsNil()
    {
        var service = new InMemoryTemplateService(new Dictionary<string, string>());
        var context = CreateContext(service);

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("not-found"),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);
    }

    [Fact]
    public async Task IncludePrompt_NoServiceProvider_ReturnsNil()
    {
        var context = new TemplateContext();

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("anything"),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);
    }

    [Fact]
    public async Task IncludePrompt_NoTemplateService_ReturnsNil()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var context = new TemplateContext();
        context.AmbientValues["ServiceProvider"] = sp;

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("anything"),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);
    }

    [Fact]
    public async Task IncludePrompt_CycleDetected_ReturnsNil()
    {
        // Pre-seed the include stack to simulate that "loop" is already being included
        // higher up the call chain. The filter must refuse to recurse into the same id.
        var service = new InMemoryTemplateService(new Dictionary<string, string>
        {
            ["loop"] = "should not render",
        });

        var context = CreateContext(service);
        context.AmbientValues["__include_prompt_stack"] = new List<string> { "LOOP" };

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("loop"),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);

        // Stack must be left in a consistent state.
        var stack = (List<string>)context.AmbientValues["__include_prompt_stack"];
        Assert.Single(stack);
        Assert.Equal("LOOP", stack[0]);
    }

    [Fact]
    public async Task IncludePrompt_DepthLimitExceeded_ReturnsNil()
    {
        var service = new InMemoryTemplateService(new Dictionary<string, string>
        {
            ["any"] = "should not render",
        });

        var context = CreateContext(service);

        // Pre-fill the stack to the maximum depth so the next include is refused.
        var seeded = new List<string>();
        for (var i = 0; i < IncludeTemplateFilter.MaxIncludeDepth; i++)
        {
            seeded.Add($"frame-{i}");
        }
        context.AmbientValues["__include_prompt_stack"] = seeded;

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("any"),
            new FilterArguments(),
            context);

        Assert.Same(NilValue.Instance, result);

        // The pre-existing stack frames must remain intact.
        var stack = (List<string>)context.AmbientValues["__include_prompt_stack"];
        Assert.Equal(IncludeTemplateFilter.MaxIncludeDepth, stack.Count);
    }

    [Fact]
    public async Task IncludePrompt_AfterRender_PopsStack()
    {
        var service = new InMemoryTemplateService(new Dictionary<string, string>
        {
            ["a"] = "A-content",
        });

        var context = CreateContext(service);

        var result = await IncludeTemplateFilter.IncludePromptAsync(
            new StringValue("a"),
            new FilterArguments(),
            context);

        Assert.Equal("A-content", result.ToStringValue());

        var stack = (List<string>)context.AmbientValues["__include_prompt_stack"];
        Assert.Empty(stack);
    }

    [Fact]
    public async Task IncludePrompt_NestedTemplateServiceRender_DetectsCycleAcrossContexts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTemplating(options =>
        {
            options.AddTemplate("a", """A {{ "b" | include_prompt }}""");
            options.AddTemplate("b", """B {{ "a" | include_prompt }}""");
        });

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITemplateService>();

        var result = await service.RenderAsync("a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("A B", NormalizeWhitespace(result));
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static TemplateContext CreateContext(ITemplateService service)
    {
        var sp = new ServiceCollection()
            .AddSingleton(service)
            .BuildServiceProvider();

        var context = new TemplateContext();
        context.AmbientValues["ServiceProvider"] = sp;

        return context;
    }

    private sealed class InMemoryTemplateService : ITemplateService
    {
        private readonly IDictionary<string, string> _templates;

        public InMemoryTemplateService(IDictionary<string, string> templates)
        {
            _templates = templates;
        }

        public Task<CrestApps.Core.Templates.Models.Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (_templates.TryGetValue(id, out var content))
            {
                return Task.FromResult(new CrestApps.Core.Templates.Models.Template { Id = id, Content = content });
            }

            return Task.FromResult<CrestApps.Core.Templates.Models.Template>(null);
        }

        public Task<IReadOnlyList<CrestApps.Core.Templates.Models.Template>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_templates.TryGetValue(id, out var content) ? content : null);
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
