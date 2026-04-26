using System.Text;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Sftp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Renci.SshNet;

namespace CrestApps.Core.AI.Mcp.Sftp.Handlers;

public sealed class SftpResourceTypeHandler : McpResourceTypeHandlerBase
{
    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<SftpResourceTypeHandler> _logger;

    public SftpResourceTypeHandler(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SftpResourceTypeHandler> logger)
        : base(SftpResourceConstants.Type)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    protected override async Task<ReadResourceResult> GetResultAsync(McpResource resource, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        if (!resource.TryGet<SftpConnectionMetadata>(out var metadata))
        {
            return CreateErrorResult(resource.Resource.Uri, "SFTP connection metadata is missing.");
        }

        var host = metadata.Host;

        if (string.IsNullOrEmpty(host))
        {
            return CreateErrorResult(resource.Resource.Uri, "SFTP host is required in the connection metadata.");
        }

        var protector = _dataProtectionProvider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);
        var port = metadata.Port ?? 22;
        var username = metadata.Username;
        var rawPath = variables.TryGetValue("path", out var pathValue) ? pathValue : string.Empty;
        var remotePath = "/" + SanitizePath(rawPath);

        var password = DataProtectionHelper.Unprotect(protector, metadata.Password, _logger, "Failed to unprotect SFTP {FieldName} for resource {ResourceId}", "password", resource.ItemId);
        var privateKey = DataProtectionHelper.Unprotect(protector, metadata.PrivateKey, _logger, "Failed to unprotect SFTP {FieldName} for resource {ResourceId}", "private key", resource.ItemId);
        var passphrase = DataProtectionHelper.Unprotect(protector, metadata.Passphrase, _logger, "Failed to unprotect SFTP {FieldName} for resource {ResourceId}", "passphrase", resource.ItemId);

        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(privateKey))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
            var privateKeyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, passphrase);
            authMethods.Add(new PrivateKeyAuthenticationMethod(username, privateKeyFile));
        }

        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (authMethods.Count == 0)
        {
            return CreateErrorResult(resource.Resource.Uri, "No authentication method provided. Please provide a password or private key.");
        }

        var connectionInfo = new ConnectionInfo(host, port, username, authMethods.ToArray());

        if (metadata.ConnectionTimeout is > 0)
        {
            connectionInfo.Timeout = TimeSpan.FromSeconds(metadata.ConnectionTimeout.Value);
        }

        if (metadata.KeepAliveInterval is > 0)
        {
            connectionInfo.ChannelCloseTimeout = TimeSpan.FromSeconds(metadata.KeepAliveInterval.Value);
        }

        using var client = new SftpClient(connectionInfo);

        if (metadata.KeepAliveInterval is > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(metadata.KeepAliveInterval.Value);
        }

        await Task.Run(client.Connect, cancellationToken);

        try
        {
            using var stream = new MemoryStream();
            client.DownloadFile(remotePath, stream);
            stream.Position = 0;

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            var mimeType = resource.Resource?.MimeType;
            if (string.IsNullOrEmpty(mimeType) && !_contentTypeProvider.TryGetContentType(remotePath, out mimeType))
            {
                mimeType = "application/octet-stream";
            }

            return new ReadResourceResult
            {
                Contents =
                [
                    new TextResourceContents
                    {
                        Uri = resource.Resource.Uri,
                        MimeType = mimeType,
                        Text = content,
                    }
                ]
            };
        }
        finally
        {
            client.Disconnect();
        }
    }
}
