using CrestApps.Core.AI.OpenAI.Azure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.OpenAI.Azure;

internal sealed class AzureClientOptionsConfiguration : IConfigureOptions<AzureClientOptions>
{
    private readonly IConfiguration _configuration;

    public AzureClientOptionsConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(AzureClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _configuration.GetSection("CrestApps:AI:AzureClient").Bind(options);
    }
}
