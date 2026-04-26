using CrestApps.Core.AI.OpenAI.Azure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.OpenAI.Azure;

internal sealed class AzureClientOptionsConfiguration : IConfigureOptions<AzureClientOptions>
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureClientOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public AzureClientOptionsConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Configures the operation.
    /// </summary>
    /// <param name="options">The options.</param>
    public void Configure(AzureClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _configuration.GetSection("CrestApps:AI:AzureClient").Bind(options);
    }
}
