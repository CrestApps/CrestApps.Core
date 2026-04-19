using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.OpenAI.Azure.Handlers;

internal sealed class AzureOpenAIConnectionHandler : AIProviderConnectionHandlerBase
{
    private const string ConnectionProtectorName = "AIProviderConnection";

    private readonly IDataProtectionProvider _dataProtectionProvider;

    public AzureOpenAIConnectionHandler(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public override void Initializing(InitializingAIProviderConnectionContext context)
    {
        if (!string.Equals(context.Connection.ClientName, AzureOpenAIConstants.ClientName, StringComparison.Ordinal))
        {
            return;
        }

        if (!context.Connection.Has<AzureConnectionMetadata>())
        {
            return;
        }

        var metadata = context.Connection.GetOrCreate<AzureConnectionMetadata>();

        context.Values["Endpoint"] = metadata.Endpoint?.ToString();
        context.Values["AuthenticationType"] = metadata.AuthenticationType.ToString();
        context.Values["IdentityId"] = metadata.IdentityId;

        if (!string.IsNullOrEmpty(metadata.ApiKey))
        {
            var protector = _dataProtectionProvider.CreateProtector(ConnectionProtectorName);

            context.Values["ApiKey"] = protector.Unprotect(metadata.ApiKey);
        }
    }
}
