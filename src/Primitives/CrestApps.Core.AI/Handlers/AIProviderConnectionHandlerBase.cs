using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Represents the AI Provider Connection Handler Base.
/// </summary>
public abstract class AIProviderConnectionHandlerBase : IAIProviderConnectionHandler
{
    /// <summary>
    /// Exportings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public virtual void Exporting(ExportingAIProviderConnectionContext context)
    {
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public virtual void Initializing(InitializingAIProviderConnectionContext context)
    {
    }
}
