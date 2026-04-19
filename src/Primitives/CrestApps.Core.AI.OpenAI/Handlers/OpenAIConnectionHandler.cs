using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.OpenAI.Handlers;

internal sealed class OpenAIConnectionHandler : AIProviderConnectionHandlerBase
{
    private const string ConnectionProtectorName = "AIProviderConnection";

    private readonly IDataProtectionProvider _dataProtectionProvider;

    public OpenAIConnectionHandler(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public override void Initializing(InitializingAIProviderConnectionContext context)
    {
        if (!string.Equals(context.Connection.ClientName, OpenAIConstants.ClientName, StringComparison.Ordinal))
        {
            return;
        }

        if (!context.Connection.TryGet<OpenAIConnectionMetadata>(out var metadata))
        {
            return;
        }

        if (!string.IsNullOrEmpty(metadata.ApiKey))
        {
            var protector = _dataProtectionProvider.CreateProtector(ConnectionProtectorName);

            context.Values["ApiKey"] = protector.Unprotect(metadata.ApiKey);
        }

        context.Values["Endpoint"] = metadata.Endpoint?.ToString();
    }
}
