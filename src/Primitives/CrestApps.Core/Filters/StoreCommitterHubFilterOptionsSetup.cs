using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Filters;

internal sealed class StoreCommitterHubFilterOptionsSetup : IConfigureOptions<HubOptions>
{
    /// <summary>
    /// Configures the operation.
    /// </summary>
    /// <param name="options">The options.</param>
    public void Configure(HubOptions options)
    {
        options.AddFilter<StoreCommitterHubFilter>();
    }
}
