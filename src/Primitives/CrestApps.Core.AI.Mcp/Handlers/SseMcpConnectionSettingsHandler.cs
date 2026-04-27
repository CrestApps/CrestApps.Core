using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Mcp.Handlers;

internal sealed class SseMcpConnectionSettingsHandler : CatalogEntryHandlerBase<McpConnection>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseMcpConnectionSettingsHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    public SseMcpConnectionSettingsHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IDataProtectionProvider dataProtectionProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<McpConnection> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task UpdatingAsync(UpdatingContext<McpConnection> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Initializeds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<McpConnection> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<McpConnection> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task ValidatingAsync(ValidatingContext<McpConnection> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.DisplayText))
        {
            context.Result.Fail(new ValidationResult("Display text is required.", [nameof(McpConnection.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Source))
        {
            context.Result.Fail(new ValidationResult("Source is required.", [nameof(McpConnection.Source)]));

            return Task.CompletedTask;
        }

        if (string.Equals(context.Model.Source, McpConstants.TransportTypes.Sse, StringComparison.Ordinal))
        {
            ValidateSseConnection(context.Model, context.Result);
        }
        else if (string.Equals(context.Model.Source, McpConstants.TransportTypes.StdIo, StringComparison.Ordinal))
        {
            ValidateStdIoConnection(context.Model, context.Result);
        }

        return Task.CompletedTask;
    }

    private void EnsureCreatedDefaults(McpConnection connection)
    {
        if (connection.CreatedUtc == default)
        {
            connection.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        connection.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        connection.Author ??= user.Identity?.Name;
    }

    private Task PopulateAsync(McpConnection connection, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(McpConnection.DisplayText), value => connection.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(McpConnection.Source), value => connection.Source = value);
        json.TryUpdateTrimmedStringValue(nameof(McpConnection.OwnerId), value => connection.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(McpConnection.Author), value => connection.Author = value);

        if (json.TryGetDateTimeValue(nameof(McpConnection.CreatedUtc), out var createdUtc))
        {
            connection.CreatedUtc = createdUtc;
        }

        if (string.Equals(connection.Source, McpConstants.TransportTypes.Sse, StringComparison.Ordinal))
        {
            PopulateSseConnection(connection, json);
        }
        else if (string.Equals(connection.Source, McpConstants.TransportTypes.StdIo, StringComparison.Ordinal))
        {
            PopulateStdIoConnection(connection, json);
        }

        return Task.CompletedTask;
    }

    private void PopulateSseConnection(McpConnection connection, JsonObject json)
    {
        var metadataNode = GetMetadataNode<SseMcpConnectionMetadata>(json);
        var metadata = connection.GetOrCreate<SseMcpConnectionMetadata>();
        var protector = _dataProtectionProvider.CreateProtector(McpConstants.DataProtectionPurpose);
        var existingApiKey = metadata.ApiKey;
        var existingBasicPassword = metadata.BasicPassword;
        var existingOAuth2ClientSecret = metadata.OAuth2ClientSecret;
        var existingOAuth2PrivateKey = metadata.OAuth2PrivateKey;
        var existingCertificate = metadata.OAuth2ClientCertificate;
        var existingCertificatePassword = metadata.OAuth2ClientCertificatePassword;

        if (json.TryGetUriValue(metadataNode, nameof(SseMcpConnectionMetadata.Endpoint), out var endpoint))
        {
            metadata.Endpoint = endpoint;
        }

        if (json.TryGetEnumValue(metadataNode, nameof(SseMcpConnectionMetadata.AuthenticationType), out ClientAuthenticationType authenticationType))
        {
            metadata.AuthenticationType = authenticationType;
        }

        metadata.ApiKeyHeaderName = null;
        metadata.ApiKeyPrefix = null;
        metadata.ApiKey = null;
        metadata.BasicUsername = null;
        metadata.BasicPassword = null;
        metadata.OAuth2TokenEndpoint = null;
        metadata.OAuth2ClientId = null;
        metadata.OAuth2ClientSecret = null;
        metadata.OAuth2Scopes = null;
        metadata.OAuth2PrivateKey = null;
        metadata.OAuth2KeyId = null;
        metadata.OAuth2ClientCertificate = null;
        metadata.OAuth2ClientCertificatePassword = null;
        metadata.AdditionalHeaders = null;

        switch (metadata.AuthenticationType)
        {
            case ClientAuthenticationType.Anonymous:
                break;
            case ClientAuthenticationType.ApiKey:
                json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.ApiKeyHeaderName), value => metadata.ApiKeyHeaderName = value);
                json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.ApiKeyPrefix), value => metadata.ApiKeyPrefix = value);
                metadata.ApiKey = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.ApiKey)), existingApiKey, protector);
                break;
            case ClientAuthenticationType.Basic:
                json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.BasicUsername), value => metadata.BasicUsername = value);
                metadata.BasicPassword = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.BasicPassword)), existingBasicPassword, protector);
                break;
            case ClientAuthenticationType.OAuth2ClientCredentials:
                PopulateOAuthCommon(metadata, json, metadataNode);
                metadata.OAuth2ClientSecret = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientSecret)), existingOAuth2ClientSecret, protector);
                break;
            case ClientAuthenticationType.OAuth2PrivateKeyJwt:
                PopulateOAuthCommon(metadata, json, metadataNode);
                json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.OAuth2KeyId), value => metadata.OAuth2KeyId = value);
                metadata.OAuth2PrivateKey = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2PrivateKey)), existingOAuth2PrivateKey, protector);
                break;
            case ClientAuthenticationType.OAuth2Mtls:
                PopulateOAuthCommon(metadata, json, metadataNode);
                metadata.OAuth2ClientCertificate = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientCertificate)), existingCertificate, protector);
                metadata.OAuth2ClientCertificatePassword = ProtectOrReuse(GetStringValue(json, metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientCertificatePassword)), existingCertificatePassword, protector);
                break;
            case ClientAuthenticationType.CustomHeaders:
                if (json.TryGetDictionaryValue(metadataNode, nameof(SseMcpConnectionMetadata.AdditionalHeaders), out var headers))
                {
                    metadata.AdditionalHeaders = headers;
                }

                break;
        }

        connection.Put(metadata);
    }

    private static void PopulateStdIoConnection(McpConnection connection, JsonObject json)
    {
        var metadataNode = GetMetadataNode<StdioMcpConnectionMetadata>(json);
        var metadata = connection.GetOrCreate<StdioMcpConnectionMetadata>();

        json.TryUpdateTrimmedStringValue(metadataNode, nameof(StdioMcpConnectionMetadata.Command), value => metadata.Command = value);
        json.TryUpdateTrimmedStringValue(metadataNode, nameof(StdioMcpConnectionMetadata.WorkingDirectory), value => metadata.WorkingDirectory = value);

        if (json.TryGetStringArrayValue(metadataNode, nameof(StdioMcpConnectionMetadata.Arguments), out var arguments))
        {
            metadata.Arguments = arguments;
        }

        if (json.TryGetDictionaryValue(metadataNode, nameof(StdioMcpConnectionMetadata.EnvironmentVariables), out var environmentVariables))
        {
            metadata.EnvironmentVariables = environmentVariables;
        }

        connection.Put(metadata);
    }

    private static void ValidateSseConnection(McpConnection connection, ValidationResultDetails result)
    {
        var metadata = connection.GetOrCreate<SseMcpConnectionMetadata>();

        if (metadata.Endpoint == null)
        {
            result.Fail(new ValidationResult("Endpoint is required.", [nameof(SseMcpConnectionMetadata.Endpoint)]));
        }

        switch (metadata.AuthenticationType)
        {
            case ClientAuthenticationType.ApiKey:
                if (string.IsNullOrWhiteSpace(metadata.ApiKey))
                {
                    result.Fail(new ValidationResult("API key is required.", [nameof(SseMcpConnectionMetadata.ApiKey)]));
                }

                break;
            case ClientAuthenticationType.Basic:
                if (string.IsNullOrWhiteSpace(metadata.BasicUsername))
                {
                    result.Fail(new ValidationResult("Basic username is required.", [nameof(SseMcpConnectionMetadata.BasicUsername)]));
                }

                if (string.IsNullOrWhiteSpace(metadata.BasicPassword))
                {
                    result.Fail(new ValidationResult("Basic password is required.", [nameof(SseMcpConnectionMetadata.BasicPassword)]));
                }

                break;
            case ClientAuthenticationType.OAuth2ClientCredentials:
                ValidateOAuthCommon(metadata, result);

                if (string.IsNullOrWhiteSpace(metadata.OAuth2ClientSecret))
                {
                    result.Fail(new ValidationResult("OAuth2 client secret is required.", [nameof(SseMcpConnectionMetadata.OAuth2ClientSecret)]));
                }

                break;
            case ClientAuthenticationType.OAuth2PrivateKeyJwt:
                ValidateOAuthCommon(metadata, result);

                if (string.IsNullOrWhiteSpace(metadata.OAuth2PrivateKey))
                {
                    result.Fail(new ValidationResult("OAuth2 private key is required.", [nameof(SseMcpConnectionMetadata.OAuth2PrivateKey)]));
                }

                break;
            case ClientAuthenticationType.OAuth2Mtls:
                ValidateOAuthCommon(metadata, result);

                if (string.IsNullOrWhiteSpace(metadata.OAuth2ClientCertificate))
                {
                    result.Fail(new ValidationResult("OAuth2 client certificate is required.", [nameof(SseMcpConnectionMetadata.OAuth2ClientCertificate)]));
                }

                break;
            default:
                break;
        }
    }

    private static void ValidateStdIoConnection(McpConnection connection, ValidationResultDetails result)
    {
        var metadata = connection.GetOrCreate<StdioMcpConnectionMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.Command))
        {
            result.Fail(new ValidationResult("Command is required.", [nameof(StdioMcpConnectionMetadata.Command)]));
        }
    }

    private static void ValidateOAuthCommon(SseMcpConnectionMetadata metadata, ValidationResultDetails result)
    {
        if (string.IsNullOrWhiteSpace(metadata.OAuth2TokenEndpoint))
        {
            result.Fail(new ValidationResult("OAuth2 token endpoint is required.", [nameof(SseMcpConnectionMetadata.OAuth2TokenEndpoint)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.OAuth2ClientId))
        {
            result.Fail(new ValidationResult("OAuth2 client ID is required.", [nameof(SseMcpConnectionMetadata.OAuth2ClientId)]));
        }
    }

    private static void PopulateOAuthCommon(
        SseMcpConnectionMetadata metadata,
        JsonObject json,
        JsonObject metadataNode)
    {
        json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.OAuth2TokenEndpoint), value => metadata.OAuth2TokenEndpoint = value);
        json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.OAuth2ClientId), value => metadata.OAuth2ClientId = value);
        json.TryUpdateTrimmedStringValue(metadataNode, nameof(SseMcpConnectionMetadata.OAuth2Scopes), value => metadata.OAuth2Scopes = value);
    }

    private static JsonObject GetMetadataNode<TMetadata>(JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(McpConnection.Properties), out var propertiesNode) || propertiesNode is null)
        {
            return null;
        }

        _ = propertiesNode.TryGetObjectValue(typeof(TMetadata).Name, out var metadataNode);

        return metadataNode;
    }

    private static string GetStringValue(JsonObject json, JsonObject metadataNode, string propertyName)
    {
        return json.TryGetTrimmedStringValue(metadataNode, propertyName, out var value) ? value : null;
    }

    private static string ProtectOrReuse(string newValue, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(newValue)
            ? existingValue
            : protector.Protect(newValue);
    }
}
