using System.Text.Json.Nodes;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.Mcp.Handlers;

internal sealed class SseMcpConnectionSettingsHandler : CatalogEntryHandlerBase<McpConnection>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public SseMcpConnectionSettingsHandler(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public override Task InitializingAsync(InitializingContext<McpConnection> context)
        => ProtectSensitiveFieldsAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<McpConnection> context)
        => ProtectSensitiveFieldsAsync(context.Model, context.Data);

    private Task ProtectSensitiveFieldsAsync(McpConnection connection, JsonNode data)
    {
        if (!string.Equals(connection.Source, McpConstants.TransportTypes.Sse, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var metadataNode = data[nameof(McpConnection.Properties)]?[nameof(SseMcpConnectionMetadata)]?.AsObject();

        if (metadataNode is null || metadataNode.Count == 0)
        {
            return Task.CompletedTask;
        }

        var protector = _dataProtectionProvider.CreateProtector(McpConstants.DataProtectionPurpose);
        var metadata = connection.As<SseMcpConnectionMetadata>();

        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.ApiKey), value => metadata.ApiKey = value);
        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.BasicPassword), value => metadata.BasicPassword = value);
        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientSecret), value => metadata.OAuth2ClientSecret = value);
        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2PrivateKey), value => metadata.OAuth2PrivateKey = value);
        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientCertificate), value => metadata.OAuth2ClientCertificate = value);
        ProtectField(protector, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientCertificatePassword), value => metadata.OAuth2ClientCertificatePassword = value);

        connection.Put(metadata);

        return Task.CompletedTask;
    }

    private static void ProtectField(IDataProtector protector, JsonObject node, string fieldName, Action<string> setter)
    {
        var value = node[fieldName]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(value))
        {
            setter(protector.Protect(value));
        }
    }
}
