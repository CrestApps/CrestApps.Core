using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Decides whether AI usage is metered. Usage is metered when something is registered to receive it, which is
/// what <c>AddCoreAIUsageTracking()</c> does.
/// </summary>
internal static class AIUsageTracking
{
    /// <summary>
    /// Gets a value indicating whether an <see cref="IAICompletionUsageObserver"/> is registered. The container
    /// answers from its registrations, so nothing is constructed to find out.
    /// </summary>
    /// <param name="serviceProvider">The service provider to ask.</param>
    public static bool IsEnabled(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IAICompletionUsageService)) == true;
    }

    /// <summary>
    /// Gets everything that receives a usage record: the registered usage service, then any other observers.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve from.</param>
    public static List<IAICompletionUsageObserver> GetObservers(IServiceProvider serviceProvider)
    {
        var observers = new List<IAICompletionUsageObserver>();

        if (serviceProvider.GetService<IAICompletionUsageService>() is { } usageService)
        {
            observers.Add(usageService);
        }

        foreach (var observer in serviceProvider.GetServices<IAICompletionUsageObserver>())
        {
            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }
        }

        return observers;
    }
}
