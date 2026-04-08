using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Filters;

internal sealed class StoreCommitterHubFilterOptionsSetup : IConfigureOptions<HubOptions>
{
    public void Configure(HubOptions options)
    {
        options.AddFilter<StoreCommitterHubFilter>();
    }
}
