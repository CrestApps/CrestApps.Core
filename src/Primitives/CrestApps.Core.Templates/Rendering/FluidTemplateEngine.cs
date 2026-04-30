using CrestApps.Core.Templates.Tags;
using Fluid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FluidTemplateOptions = Fluid.TemplateOptions;

namespace CrestApps.Core.Templates.Rendering;

/// <summary>
/// Processes Liquid templates using the Fluid template engine.
/// Member access on POCO arguments is governed by the configured
/// <see cref="FluidTemplateOptions.MemberAccessStrategy"/>; the framework
/// defaults to <see cref="DefaultMemberAccessStrategy"/> (deny by default).
/// Consumers must register types they wish to expose via
/// <c>services.Configure&lt;Fluid.TemplateOptions&gt;(o =&gt; o.MemberAccessStrategy.Register&lt;MyType&gt;())</c>.
/// </summary>
public sealed class FluidTemplateEngine : ITemplateEngine
{
    private static readonly FluidParser _parser = CreateParser();

    /// <summary>
    /// Ambient key used to flow the active <see cref="CancellationToken"/> through Fluid render contexts.
    /// Custom tags and filters that perform asynchronous work should observe this token.
    /// </summary>
    internal const string CancellationTokenAmbientKey = "__crestapps_cancellation_token";

    private readonly IServiceProvider _serviceProvider;
    private readonly FluidTemplateOptions _options;
    private readonly ILogger<FluidTemplateEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FluidTemplateEngine"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The Fluid template options.</param>
    /// <param name="logger">The logger.</param>
    public FluidTemplateEngine(
        IServiceProvider serviceProvider,
        IOptions<FluidTemplateOptions> options,
        ILogger<FluidTemplateEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options?.Value ?? new FluidTemplateOptions();
        _logger = logger;
    }

    /// <summary>
    /// Renders a Liquid template using the configured Fluid engine.
    /// </summary>
    /// <param name="template">The template content.</param>
    /// <param name="arguments">The template arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered output.</returns>
    public async Task<string> RenderAsync(string template, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        if (!_parser.TryParse(template, out var fluidTemplate, out var error))
        {
            _logger.LogWarning("Failed to parse Liquid template: {Error}", error);

            return template;
        }

        var context = new TemplateContext(_options);
        context.AmbientValues["ServiceProvider"] = _serviceProvider;
        context.AmbientValues[RenderTemplateTag.AmbientFluidParserKey] = _parser;
        context.AmbientValues[CancellationTokenAmbientKey] = cancellationToken;

        if (arguments != null)
        {
            foreach (var (key, value) in arguments)
            {
                context.SetValue(key, value);
            }
        }

        var result = await fluidTemplate.RenderAsync(context);

        cancellationToken.ThrowIfCancellationRequested();

        return NormalizeWhitespace(result);
    }

    /// <summary>
    /// Validates that the supplied Liquid template has valid syntax.
    /// </summary>
    /// <param name="template">The template content.</param>
    /// <param name="errors">The validation errors, if any.</param>
    /// <returns><see langword="true"/> if the template is valid; otherwise, <see langword="false"/>.</returns>
    public bool TryValidate(string template, out IList<string> errors)
    {
        errors = [];

        if (string.IsNullOrWhiteSpace(template))
        {
            return true;
        }

        if (!_parser.TryParse(template, out _, out var error))
        {
            errors.Add(error);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes whitespace in the rendered output so templates can be written
    /// with readable formatting while producing clean output for AI consumption.
    /// Collapses runs of blank lines into a single blank line and trims each line.
    /// </summary>
    internal static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Split('\n');
        var builder = new List<string>(lines.Length);
        var previousWasBlank = true;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimEnd('\r').Trim();

            if (trimmed.Length == 0)
            {
                if (!previousWasBlank)
                {
                    builder.Add(string.Empty);
                    previousWasBlank = true;
                }

                continue;
            }

            builder.Add(trimmed);
            previousWasBlank = false;
        }

        // Remove trailing blank lines.
        while (builder.Count > 0 && builder[^1].Length == 0)
        {
            builder.RemoveAt(builder.Count - 1);
        }

        return string.Join('\n', builder);
    }

    private static FluidParser CreateParser()
    {
        var parser = new FluidParser();

        parser.RegisterExpressionTag(
            RenderTemplateTag.TagName,
            RenderTemplateTag.WriteToAsync);

        return parser;
    }
}
