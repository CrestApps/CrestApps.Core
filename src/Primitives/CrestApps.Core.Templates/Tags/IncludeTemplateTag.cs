using CrestApps.Core.Templates.Services;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Templates.Tags;

/// <summary>
/// Fluid filter that includes the rendered content of another prompt template.
/// Usage: <c>{{ "prompt-id" | include_prompt }}</c>
/// </summary>
/// <remarks>
/// Includes are bounded by <see cref="MaxIncludeDepth"/> and tracked via an
/// active include stack that flows across nested renders and is mirrored into
/// <see cref="TemplateContext.AmbientValues"/>.
/// Cyclic inclusion or exceeding the depth budget short-circuits to
/// <see cref="NilValue.Instance"/> to defend against runaway recursion or
/// crafted templates that try to walk the prompt graph.
/// </remarks>
public static class IncludeTemplateFilter
{
    /// <summary>The Liquid filter name.</summary>
    public const string FilterName = "include_prompt";

    /// <summary>Maximum nesting depth for <c>include_prompt</c> chains.</summary>
    public const int MaxIncludeDepth = 10;

    private const string IncludeStackKey = "__include_prompt_stack";
    private static readonly AsyncLocal<List<string>> _asyncIncludeStack = new();

    /// <summary>
    /// Filter implementation invoked by the Fluid engine.
    /// </summary>
    /// <param name="input">The input value containing the template identifier.</param>
    /// <param name="arguments">The filter arguments.</param>
    /// <param name="context">The Fluid template context.</param>
    /// <returns>The rendered template content, or <see cref="NilValue.Instance"/> when resolution fails.</returns>
    public static ValueTask<FluidValue> IncludePromptAsync(
        FluidValue input,
        FilterArguments arguments,
        TemplateContext context)
    {
        return ResolveAsync(input, context);
    }

    /// <summary>
    /// Resolves and renders the included template for the supplied prompt identifier.
    /// </summary>
    /// <param name="input">The input value containing the template identifier.</param>
    /// <param name="context">The Fluid template context.</param>
    /// <returns>The rendered template content, or <see cref="NilValue.Instance"/> when resolution fails.</returns>
    private static async ValueTask<FluidValue> ResolveAsync(FluidValue input, TemplateContext context)
    {
        var promptId = input?.ToStringValue();

        if (string.IsNullOrWhiteSpace(promptId))
        {
            return NilValue.Instance;
        }

        var ownsAsyncStack = _asyncIncludeStack.Value is null;
        GetOrCreateStack(context);
        var pushed = false;

        try
        {
            if (!TryEnterTemplateFrame(promptId, ownsAsyncStack, allowCurrentFrame: false, out pushed))
            {
                return NilValue.Instance;
            }

            if (!context.AmbientValues.TryGetValue("ServiceProvider", out var sp) ||
                sp is not IServiceProvider serviceProvider)
            {
                return NilValue.Instance;
            }

            var service = serviceProvider.GetService<ITemplateService>();

            if (service is null)
            {
                return NilValue.Instance;
            }

            var rendered = await service.RenderAsync(promptId, arguments: null, RenderTemplateTag.GetCancellationToken(context));

            if (rendered is null)
            {
                return NilValue.Instance;
            }

            return new StringValue(rendered);
        }
        finally
        {
            ExitTemplateFrame(pushed, ownsAsyncStack);
        }
    }

    /// <summary>
    /// Attempts to enter a template frame for the specified prompt identifier.
    /// </summary>
    /// <param name="promptId">The prompt identifier.</param>
    /// <param name="pushed"><see langword="true"/> when a new frame was added; otherwise, <see langword="false"/>.</param>
    /// <param name="ownsAsyncStack"><see langword="true"/> when the current call created the async stack; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the frame can be entered; otherwise, <see langword="false"/>.</returns>
    internal static bool TryEnterTemplateFrame(string promptId, out bool pushed, out bool ownsAsyncStack)
    {
        ownsAsyncStack = _asyncIncludeStack.Value is null;

        return TryEnterTemplateFrame(promptId, ownsAsyncStack, allowCurrentFrame: true, out pushed);
    }

    /// <summary>
    /// Attempts to enter a template frame while controlling reuse of the current frame.
    /// </summary>
    /// <param name="promptId">The prompt identifier.</param>
    /// <param name="ownsAsyncStack"><see langword="true"/> when the current call owns the async stack.</param>
    /// <param name="allowCurrentFrame"><see langword="true"/> to allow reuse of the current frame when it already matches <paramref name="promptId"/>.</param>
    /// <param name="pushed"><see langword="true"/> when a new frame was added; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the frame can be entered; otherwise, <see langword="false"/>.</returns>
    internal static bool TryEnterTemplateFrame(string promptId, bool ownsAsyncStack, bool allowCurrentFrame, out bool pushed)
    {
        var stack = _asyncIncludeStack.Value ??= [];
        pushed = false;

        if (allowCurrentFrame && stack.Count > 0 && string.Equals(stack[^1], promptId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (stack.Count >= MaxIncludeDepth)
        {
            return false;
        }

        if (stack.Contains(promptId, StringComparer.OrdinalIgnoreCase))
        {
            // Cycle detected; refuse to render to avoid unbounded recursion or
            // crafted templates that bounce between prompts.
            return false;
        }

        stack.Add(promptId);
        pushed = true;

        return true;
    }

    /// <summary>
    /// Exits the current template frame and clears the async stack when owned by the caller.
    /// </summary>
    /// <param name="pushed"><see langword="true"/> when the current call added a frame; otherwise, <see langword="false"/>.</param>
    /// <param name="ownsAsyncStack"><see langword="true"/> when the current call owns the async stack; otherwise, <see langword="false"/>.</param>
    internal static void ExitTemplateFrame(bool pushed, bool ownsAsyncStack)
    {
        var stack = _asyncIncludeStack.Value;

        if (pushed && stack is { Count: > 0 })
        {
            stack.RemoveAt(stack.Count - 1);
        }

        if (ownsAsyncStack)
        {
            _asyncIncludeStack.Value = null;
        }
    }

    /// <summary>
    /// Gets the active include stack from the template context or creates one when needed.
    /// </summary>
    /// <param name="context">The Fluid template context.</param>
    /// <returns>The active include stack.</returns>
    private static List<string> GetOrCreateStack(TemplateContext context)
    {
        if (context.AmbientValues.TryGetValue(IncludeStackKey, out var existing) && existing is List<string> stack)
        {
            _asyncIncludeStack.Value ??= stack;

            return stack;
        }

        if (_asyncIncludeStack.Value is { } asyncStack)
        {
            context.AmbientValues[IncludeStackKey] = asyncStack;

            return asyncStack;
        }

        var created = new List<string>();
        context.AmbientValues[IncludeStackKey] = created;
        _asyncIncludeStack.Value = created;

        return created;
    }
}
