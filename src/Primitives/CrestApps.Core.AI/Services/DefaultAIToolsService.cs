using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Tools Service.
/// </summary>
public sealed class DefaultAIToolsService : IAIToolsService
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIToolsService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public DefaultAIToolsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets by name.
    /// </summary>
    /// <param name="name">The name.</param>
    public ValueTask<AITool> GetByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

return ValueTask.FromResult(_serviceProvider.GetKeyedService<AITool>(name));
    }
}
