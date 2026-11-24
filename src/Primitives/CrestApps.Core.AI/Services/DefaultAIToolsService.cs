using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Tools Service.
/// </summary>
public sealed class DefaultAIToolsService : IAIToolsService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AIToolDefinitionOptions _toolDefinitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIToolsService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="toolDefinitions">The registered AI tool definitions.</param>
    public DefaultAIToolsService(
        IServiceProvider serviceProvider,
        IOptions<AIToolDefinitionOptions> toolDefinitions)
    {
        _serviceProvider = serviceProvider;
        _toolDefinitions = toolDefinitions.Value;
    }

    /// <summary>
    /// Gets a tool by its name, or by one of its aliases: a tool is registered as a keyed service under its current
    /// name only, so a name stored before the tool was renamed is resolved first.
    /// </summary>
    /// <param name="name">The name of the tool, or one of its aliases.</param>
    public ValueTask<AITool> GetByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ValueTask.FromResult(_serviceProvider.GetKeyedService<AITool>(_toolDefinitions.ResolveToolName(name)));
    }
}
