using CrestApps.Core;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users configure calls to arbitrary HTTP
/// APIs. Each configured <see cref="AIToolInstance"/> binds a base URL, HTTP method, authentication, and
/// static headers; the AI model only supplies the open arguments the settings allow. Display metadata
/// (name, description, category) is supplied at registration time via
/// <c>AddAIToolInstanceSource&lt;HttpApiRequestToolInstanceSource&gt;(...)</c>.
/// </summary>
public sealed class HttpApiRequestToolInstanceSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="HttpApiRequestToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured HTTP request function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? functionName
            : instance.Description;

        return new HttpApiRequestToolFunction(functionName, description, settings, instance);
    }
}
