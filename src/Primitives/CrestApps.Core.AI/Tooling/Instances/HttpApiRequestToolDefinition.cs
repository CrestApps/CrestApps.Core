using CrestApps.Core;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// The built-in <see cref="IAIToolInstanceDefinition"/> that lets users configure calls to arbitrary
/// HTTP APIs. Each configured instance binds a base URL, HTTP method, authentication, and static
/// headers; the AI model only supplies the open arguments the settings allow.
/// </summary>
public sealed class HttpApiRequestToolDefinition : IAIToolInstanceDefinition
{
    /// <summary>
    /// Gets the registered definition name.
    /// </summary>
    public string Name => HttpApiRequestToolConstants.DefinitionName;

    /// <summary>
    /// Creates the <see cref="HttpApiRequestToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="context">The context describing the instance and the function metadata to expose.</param>
    /// <returns>The configured HTTP request function.</returns>
    public AITool CreateTool(AIToolInstanceToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = context.Instance.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        return new HttpApiRequestToolFunction(context.FunctionName, context.Description, settings);
    }
}
