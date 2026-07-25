using CrestApps.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Sources;

/// <summary>
/// The built-in <see cref="AIToolSource"/> that lets users configure calls to arbitrary
/// HTTP APIs. Each configured <see cref="AIToolDefinition"/> binds a base URL, HTTP method,
/// authentication, and static headers; the AI model only supplies the open arguments the settings allow.
/// </summary>
public sealed class HttpApiRequestToolSource : AIToolSource
{
    /// <summary>
    /// Gets the registered source name.
    /// </summary>
    public override string Name => HttpApiRequestToolConstants.SourceName;

    /// <summary>
    /// Gets the friendly display name shown when choosing this source to configure a new definition.
    /// </summary>
    public override LocalizedString DisplayName => new("HTTP API Request", "HTTP API Request");

    /// <summary>
    /// Gets the description explaining what definitions this source produces.
    /// </summary>
    public override LocalizedString Description => new(
        "HTTP API Request Description",
        "Call an external HTTP API with a preconfigured endpoint, method, authentication, and headers. The AI model only supplies the open arguments you allow (path, query, body).");

    /// <summary>
    /// Gets the category used to group this source in the management UI.
    /// </summary>
    public override string Category => "Integrations";

    /// <summary>
    /// Creates the <see cref="HttpApiRequestToolFunction"/> bound to the supplied definition's settings.
    /// </summary>
    /// <param name="context">The context describing the definition and the function metadata to expose.</param>
    /// <returns>The configured HTTP request function.</returns>
    public override AITool CreateTool(AIToolSourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Definition.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        return new HttpApiRequestToolFunction(context.FunctionName, context.Description, settings);
    }
}
